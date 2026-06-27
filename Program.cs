using CatClawMusicServer.Data;
using CatClawMusicServer.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ── 配置 ──
var config = builder.Configuration;
var dbPath = config["MusicServer:DbPath"] ?? "Data/catclaw.db";
var musicDir = config["MusicServer:MusicDirectory"] ?? "";
var coverDir = config["MusicServer:CoverOutputDir"] ?? "Data/covers";

// 确保目录存在
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
Directory.CreateDirectory(coverDir);

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

// ASP.NET Core MVC + Controllers
builder.Services.AddControllers()
    .AddJsonOptions(opt =>
    {
        opt.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        opt.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS（允许 localhost 开发）
builder.Services.AddCors(opt =>
{
    opt.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5173", "http://localhost:5000")
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

// ── 中间件管道 ──
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
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

app.Run();

// ── 配置选项记录 ──
public record ScannerOptions
{
    public string MusicDirectory { get; set; } = "";
    public string CoverOutputDir { get; set; } = "";
}
