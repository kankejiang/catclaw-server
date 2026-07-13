using CatClawMusicServer;
using CatClawMusicServer.ClawCircle;
using CatClawMusicServer.ClawCircle.Dht;
using CatClawMusicServer.ClawCircle.Ledger;
using CatClawMusicServer.ClawCircle.Transfer;
using CatClawMusicServer.Data;
using CatClawMusicServer.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
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
// EF Core SQLite — Factory (Singleton) + scoped DbContext (from factory)
builder.Services.AddDbContextFactory<ApplicationDbContext>(opt =>
    opt.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddScoped<ApplicationDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());

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
    new AdminCredentialStore(sp.GetRequiredService<ServerAuthOptions>(), dbPath));

// 猫爪驿站 P2P tracker 在线节点注册表（单例）
builder.Services.AddSingleton<ClawCircleTracker>();
builder.Services.AddSingleton<NodeReputation>();
builder.Services.AddSingleton<TransferEngine>();
builder.Services.AddSingleton<UdpTransferProtocol>();
builder.Services.AddSingleton<BlockchainLedger>();
builder.Services.AddSingleton<CatClawMusicServer.ClawCircle.Accounts.AccountService>();

// DHT 配置
var dhtSection = builder.Configuration.GetSection("ClawCircle");
var dhtOpts = new DhtOptions
{
    Enabled = bool.TryParse(dhtSection["DhtEnabled"], out var de) && de,
    Port = int.TryParse(dhtSection["DhtPort"], out var dp) ? dp : 37825,
    NodeIdSeed = dhtSection["NodeIdSeed"] ?? ""
};

// NodeIdSeed 持久化：配置为空时首次启动随机生成并保存到 Data/node_id.txt，
// 避免所有部署共享相同 NodeId 导致 DHT 路由错乱。
if (string.IsNullOrEmpty(dhtOpts.NodeIdSeed) || dhtOpts.NodeIdSeed == "catclaw-default-node")
{
    var nodeIdFile = Path.Combine(Path.GetDirectoryName(dbPath)!, "node_id.txt");
    if (File.Exists(nodeIdFile))
    {
        dhtOpts.NodeIdSeed = File.ReadAllText(nodeIdFile).Trim();
    }
    else
    {
        // 首次启动：生成随机种子并持久化
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        dhtOpts.NodeIdSeed = Convert.ToHexString(bytes);
        await File.WriteAllTextAsync(nodeIdFile, dhtOpts.NodeIdSeed);
        Console.WriteLine($"[clawcircle] 首次启动生成 NodeIdSeed 并持久化到 {nodeIdFile}");
    }
}

var bootstrapStr = dhtSection["BootstrapNodes"];
if (!string.IsNullOrEmpty(bootstrapStr))
    dhtOpts.BootstrapNodes = bootstrapStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
else
    dhtOpts.BootstrapNodes = new List<string>(); // 不再硬编码开发者域名
builder.Services.AddSingleton(dhtOpts);
builder.Services.AddSingleton<DhtService>(sp =>
    new DhtService(
        sp.GetRequiredService<DhtOptions>(),
        sp.GetRequiredService<ILogger<DhtService>>(),
        sp.GetRequiredService<ILogger<RoutingTable>>(),
        sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>()));

// ── JWT 认证配置 ──
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtSecretKey = jwtSection["SecretKey"] ?? "CatClawMusic-Default-Secret-Change-In-Production-2024!";
var jwtIssuer = jwtSection["Issuer"] ?? "CatClawMusicServer";
var jwtAudience = jwtSection["Audience"] ?? "CatClawMusicClient";
var accessExpireMin = int.TryParse(jwtSection["AccessTokenExpireMinutes"], out var aem) ? aem : 15;
var refreshExpireDays = int.TryParse(jwtSection["RefreshTokenExpireDays"], out var red) ? red : 30;

builder.Services.AddSingleton(new JwtOptions
{
    SecretKey = jwtSecretKey,
    Issuer = jwtIssuer,
    Audience = jwtAudience,
    AccessTokenExpireMinutes = accessExpireMin,
    RefreshTokenExpireDays = refreshExpireDays
});
builder.Services.AddSingleton<JwtService>();

// ── 流媒体配置 ──
var streamSection = builder.Configuration.GetSection("Streaming");
var streamOpts = new StreamingOptions
{
    HlsEnabled = bool.TryParse(streamSection["HlsEnabled"], out var he) && he,
    TranscodeCacheSizeGB = int.TryParse(streamSection["TranscodeCacheSizeGB"], out var tc) ? tc : 2,
    FFmpegPath = streamSection["FFmpegPath"] ?? "ffmpeg",
    SegmentDurationSeconds = int.TryParse(streamSection["SegmentDurationSeconds"], out var sd) ? sd : 6,
    MaxConcurrentTranscodes = int.TryParse(streamSection["MaxConcurrentTranscodes"], out var mc) ? mc : 4,
    TranscodeDir = streamSection["TranscodeDir"] ?? "Data/transcode"
};
var brStr = streamSection["DefaultBitrates"];
if (!string.IsNullOrEmpty(brStr))
    streamOpts.DefaultBitrates = brStr.Split(',').Select(s => int.TryParse(s.Trim(), out var b) ? b : 0).Where(b => b > 0).ToArray();

builder.Services.AddSingleton(streamOpts);
builder.Services.AddSingleton<TranscodingService>();
builder.Services.AddSingleton<CoverService>();
builder.Services.AddSingleton<EventBus>();
builder.Services.AddScoped<StatsService>();
builder.Services.AddScoped<RecommendService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        // 允许媒体端点（音频流/HLS/封面）通过查询参数 access_token 携带 JWT，
        // 因为 <audio src> / <img src> / HLS.js 默认无法设置 Authorization 头。
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var path = context.HttpContext.Request.Path.Value ?? "";
                if ((path.StartsWith("/api/v1/songs", StringComparison.OrdinalIgnoreCase) ||
                     path.StartsWith("/api/v1/hls", StringComparison.OrdinalIgnoreCase) ||
                     path.StartsWith("/api/v1/albums", StringComparison.OrdinalIgnoreCase) ||
                     path.StartsWith("/api/v1/artists", StringComparison.OrdinalIgnoreCase)) &&
                    context.Request.Query.TryGetValue("access_token", out var token) &&
                    !string.IsNullOrEmpty(token))
                {
                    context.Token = token!;
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

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
app.UseMiddleware<ClawCircleWebSocketMiddleware>(); // 猫爪驿站 WebSocket 信令（拦截 /ws/clawcircle，自带 token 鉴权）
app.UseMiddleware<ApiAuthMiddleware>();   // 内网鉴权（/api 与 /rest）
app.UseMiddleware<WebUiAuthMiddleware>(); // Web UI 静态页面鉴权（cookie 登录，未登录跳 login.html）
app.UseDefaultFiles();   // 支持 index.html 默认文件
app.UseStaticFiles();   // 服务 wwwroot 静态文件

app.UseRateLimiter();    // ← .NET 8 内置限速

app.UseAuthentication();  // JWT Bearer 认证（/api/v1/* 路由）
app.UseAuthorization();

app.MapControllers();

// ── 确保数据库创建 ──
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();
}

// ── 启动猫爪驿站 STUN（UDP 反射端点探测 + P2P 传输多路复用，端口 = HTTP 端口 + 1）──
var stunPort = (cliPort ?? ParsePort(config["Kestrel:EndPoints:HttpV4:Url"] ?? config["ASPNETCORE_URLS"] ?? "") ?? 37823) + 1;
var stunService = new CatClawMusicServer.ClawCircle.ClawCircleStunService(
    app.Services.GetRequiredService<CatClawMusicServer.ClawCircle.ClawCircleTracker>(),
    stunPort,
    app.Services.GetRequiredService<CatClawMusicServer.ClawCircle.Transfer.UdpTransferProtocol>());
stunService.Start();
app.Lifetime.ApplicationStopping.Register(() => stunService.Stop());

// ── 定时清理：信誉记录（30 天未见）+ 过期传输任务（2 小时）（每 30 分钟）──
var repCleanup = app.Services.GetRequiredService<NodeReputation>();
var engCleanup = app.Services.GetRequiredService<TransferEngine>();
var cleanTimer = new System.Threading.Timer(_ =>
{
    try { repCleanup.Cleanup(); } catch { }
    try { engCleanup.Cleanup(TimeSpan.FromHours(2)); } catch { }
}, null, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
app.Lifetime.ApplicationStopping.Register(() => cleanTimer.Dispose());

// ── 启动区块链积分账本（定时出块 + 在线奖励 + 修剪）──
var ledger = app.Services.GetRequiredService<BlockchainLedger>();
var trackerForLedger = app.Services.GetRequiredService<ClawCircleTracker>();
var accountServiceForLedger = app.Services.GetRequiredService<CatClawMusicServer.ClawCircle.Accounts.AccountService>();
ledger.SetOnlineNodesProvider(() => trackerForLedger.GetOnlineNodes());
// 注入 deviceId → accountId 解析器（异步，账本通过此回调将设备传输记账归到账号）
ledger.SetDeviceAccountResolver(async deviceId =>
{
    var acc = await accountServiceForLedger.FindByDeviceIdAsync(deviceId);
    return acc?.Id;
});
ledger.Start();
app.Lifetime.ApplicationStopping.Register(() => ledger.Stop());

// ── 启动 DHT 服务（可选，Kademlia 去中心化节点发现）──
var dhtOptions = app.Services.GetRequiredService<DhtOptions>();
if (dhtOptions.Enabled)
{
    var dhtService = app.Services.GetRequiredService<DhtService>();
    dhtService.Start();
    app.Lifetime.ApplicationStopping.Register(() => dhtService.Stop());

    // Bootstrap 连接（支持域名和 IP）
    foreach (var bootstrap in dhtOptions.BootstrapNodes)
    {
        if (!string.IsNullOrEmpty(bootstrap))
        {
            _ = dhtService.BootstrapFromAddressAsync(bootstrap);
        }
    }
}

app.Run();

// ── 命令行参数解析（本地函数）──

// ── 从 URL 字符串中解析端口号 ──
static int? ParsePort(string url)
{
    if (string.IsNullOrWhiteSpace(url)) return null;
    var lastColon = url.LastIndexOf(':');
    if (lastColon > 0 && int.TryParse(url[(lastColon + 1)..], out var p))
        return p;
    return null;
}

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
