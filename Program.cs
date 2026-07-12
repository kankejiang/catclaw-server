using CatClawMusicServer;
using CatClawMusicServer.ClawCircle;
using CatClawMusicServer.Data;
using CatClawMusicServer.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ── 配置 ──
var cli = ParseCliArgs(args);
var config = builder.Configuration;
var dbPath = config["MusicServer:DbPath"] ?? "Data/catclaw.db";
var musicDir = cli.TryGetValue("music-dir", out var md) && !string.IsNullOrWhiteSpace(md)
    ? md : (config["MusicServer:MusicDirectory"] ?? "");
var coverDir = config["MusicServer:CoverOutputDir"] ?? "Data/covers";
int? cliPort = cli.TryGetValue("port", out var ps) && int.TryParse(ps, out var pp) ? pp : null;
var accessToken = cli.TryGetValue("token", out var tk) ? tk : (config["MusicServer:AccessToken"] ?? "");
var adminUser = config["MusicServer:AdminUser"] ?? "admin";
var adminPassword = config["MusicServer:AdminPassword"] ?? "";

// 确保目录存在
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
Directory.CreateDirectory(coverDir);

// CLI 覆盖 Kestrel 端口（IPv4 + IPv6 两个端点同步）
if (cliPort.HasValue)
{
    builder.Configuration["Kestrel:EndPoints:HttpV4:Url"] = $"http://0.0.0.0:{cliPort.Value}";
    builder.Configuration["Kestrel:EndPoints:HttpV6:Url"] = $"http://[::]:{cliPort.Value}";
}

// ── 服务注册 ──
// EF Core SQLite
builder.Services.AddDbContext<ApplicationDbContext>(opt =>
    opt.UseSqlite($"Data Source={dbPath}"));

// 音乐扫描服务
builder.Services.AddScoped<MusicScanner>();
builder.Services.AddSingleton(new ScannerOptions
{
    MusicDirectory = musicDir,
    CoverOutputDir = coverDir
});

// 内网鉴权配置（AccessToken 为空则关闭鉴权，便于本地开发）
builder.Services.AddSingleton(new ServerAuthOptions { AccessToken = accessToken, AdminUser = adminUser, AdminPassword = adminPassword });

// 管理员凭据持久化存储（首次启动从 appsettings 读，注册后写入 Data/admin.json）
builder.Services.AddSingleton<AdminCredentialStore>(sp =>
    new AdminCredentialStore(sp.GetRequiredService<ServerAuthOptions>()));

// 猫爪圈 P2P tracker 在线节点注册表（单例）
builder.Services.AddSingleton<ClawCircleTracker>();

// ASP.NET Core MVC + Controllers
builder.Services.AddControllers()
    .AddJsonOptions(opt =>
    {
        opt.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        opt.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS（允许 localhost IPv4/IPv6 开发）
builder.Services.AddCors(opt =>
{
    opt.AddDefaultPolicy(policy =>
        policy.WithOrigins(
              "http://localhost:5173", "http://[::1]:5173",
              "http://localhost:37823", "http://[::1]:37823")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// 限速（.NET 8 内置）
builder.Services.AddRateLimiter(opt =>
{
    opt.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 200,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    opt.RejectionStatusCode = 429;
});

var app = builder.Build();

// ── 命令行模式：重置管理员凭据（claw reset）──
if (cli.TryGetValue("claw-reset", out _))
{
    var adminFile = Path.Combine(AppContext.BaseDirectory, "Data", "admin.json");
    if (File.Exists(adminFile))
    {
        File.Delete(adminFile);
        Console.WriteLine("[claw] 管理员凭据已重置。重启服务端后将进入首次注册流程。");
    }
    else
    {
        Console.WriteLine("[claw] 未找到管理员凭据文件（尚未注册或已重置）。");
    }
    return;
}

// ── 命令行模式：仅扫描后退出（--scan-and-exit）──
if (cli.TryGetValue("scan-and-exit", out _))
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.EnsureCreated();
        var scanner = scope.ServiceProvider.GetRequiredService<MusicScanner>();
        var opts = scope.ServiceProvider.GetRequiredService<ScannerOptions>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<MusicScanner>>();
        logger.LogInformation("（命令行模式）开始扫描目录: {Dir}", opts.MusicDirectory);
        var result = await scanner.ScanDirectoryAsync(opts.MusicDirectory, opts.CoverOutputDir, CancellationToken.None);
        logger.LogInformation("（命令行模式）扫描完成: 处理 {P}, 新增 {A}, 更新 {U}, 跳过 {S}, 错误 {E}",
            result.ProcessedCount, result.AddedCount, result.UpdatedCount, result.SkippedCount, result.ErrorCount);
        Console.WriteLine($"[scan-and-exit] 处理 {result.ProcessedCount} / 新增 {result.AddedCount} / 更新 {result.UpdatedCount} / 错误 {result.ErrorCount}");
    }
    return;
}

// ── 中间件管道 ──
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseWebSockets();  // 启用 WebSocket 升级处理（ClawCircle 信令依赖）
app.UseMiddleware<ClawCircleWebSocketMiddleware>(); // 猫爪圈 WebSocket 信令（拦截 /ws/clawcircle，自带 token 鉴权）
app.UseMiddleware<ApiAuthMiddleware>();   // 内网鉴权（/api 与 /rest）
app.UseMiddleware<WebUiAuthMiddleware>(); // Web UI 静态页面鉴权（cookie 登录，未登录跳 login.html）
app.UseDefaultFiles();   // 支持 index.html 默认文件
app.UseStaticFiles();   // 服务 wwwroot 静态文件

app.UseRateLimiter();    // ← .NET 8 内置限速

app.UseAuthorization();

app.MapControllers();

// ── 确保数据库创建 ──
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();
}

// ── 启动猫爪圈 STUN（UDP 反射端点探测，端口 = HTTP 端口 + 1）──
var stunPort = (cliPort ?? 37823) + 1;
var stunService = new CatClawMusicServer.ClawCircle.ClawCircleStunService(
    app.Services.GetRequiredService<CatClawMusicServer.ClawCircle.ClawCircleTracker>(), stunPort);
stunService.Start();
app.Lifetime.ApplicationStopping.Register(() => stunService.Stop());

app.Run();

// ── 命令行参数解析（本地函数）──
static Dictionary<string, string> ParseCliArgs(string[] args)
{
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < args.Length; i++)
    {
        var a = args[i];
        // 无前缀的子命令
        if (a == "claw" && i + 1 < args.Length && args[i + 1] == "reset") { d["claw-reset"] = "true"; i++; continue; }
        if (a.Length > 2 && a.StartsWith("--"))
        {
            var key = a.Substring(2);
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                d[key] = args[++i];
            else
                d[key] = "true";
        }
    }
    return d;
}

// ── 配置选项记录（定义在 ScannerOptions.cs 中，Program.cs 不再重复定义）──
