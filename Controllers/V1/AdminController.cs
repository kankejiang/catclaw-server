using System.IdentityModel.Tokens.Jwt;
using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using CatClawMusicServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.V1;

[Authorize]
[ApiController]
[Route("api/v1/admin")]
public class AdminController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly MusicScanner _scanner;
    private readonly ScannerOptions _scanOpts;
    private readonly StatsService _stats;
    private readonly ILogger<AdminController> _logger;

    private static bool _scanning;
    private static ScanResult? _lastResult;

    public AdminController(ApplicationDbContext db, MusicScanner scanner,
        ScannerOptions scanOpts, StatsService stats, ILogger<AdminController> logger)
    {
        _db = db;
        _scanner = scanner;
        _scanOpts = scanOpts;
        _stats = stats;
        _logger = logger;
    }

    // POST /api/v1/admin/scan
    [HttpPost("scan")]
    public IActionResult TriggerScan()
    {
        var role = JwtService.GetRole(User);
        if (role != "admin")
            return Ok(ApiResponse<object>.Error(ErrorCodes.Forbidden, "仅管理员可操作"));

        if (_scanning)
            return Ok(ApiResponse<object>.Error(ErrorCodes.InvalidParameter, "扫描正在进行中"));

        _scanning = true;
        _lastResult = null;

        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("开始全量扫描: {Dir}", _scanOpts.MusicDirectory);
                _lastResult = await _scanner.ScanDirectoryAsync(
                    _scanOpts.MusicDirectory, _scanOpts.CoverOutputDir);
                _logger.LogInformation("扫描完成: 新增{Added} 更新{Updated} 跳过{Skipped} 错误{Errors}",
                    _lastResult.AddedCount, _lastResult.UpdatedCount,
                    _lastResult.SkippedCount, _lastResult.ErrorCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "扫描失败");
            }
            finally
            {
                _scanning = false;
            }
        });

        return Ok(ApiResponse<object>.Ok(new { message = "扫描已启动" }));
    }

    // POST /api/v1/admin/scan/incremental
    [HttpPost("scan/incremental")]
    public IActionResult TriggerIncrementalScan()
    {
        var role = JwtService.GetRole(User);
        if (role != "admin")
            return Ok(ApiResponse<object>.Error(ErrorCodes.Forbidden, "仅管理员可操作"));

        if (_scanning)
            return Ok(ApiResponse<object>.Error(ErrorCodes.InvalidParameter, "扫描正在进行中"));

        _scanning = true;
        _lastResult = null;

        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("开始增量扫描: {Dir}", _scanOpts.MusicDirectory);
                _lastResult = await _scanner.IncrementalScanAsync(
                    _scanOpts.MusicDirectory, _scanOpts.CoverOutputDir);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "增量扫描失败");
            }
            finally
            {
                _scanning = false;
            }
        });

        return Ok(ApiResponse<object>.Ok(new { message = "增量扫描已启动" }));
    }

    // GET /api/v1/admin/scan/status
    [HttpGet("scan/status")]
    public async Task<IActionResult> GetScanStatus()
    {
        var songCount = await _db.Songs.CountAsync();
        var artistCount = await _db.Artists.CountAsync();
        var albumCount = await _db.Albums.CountAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            scanning = _scanning,
            last_result = _lastResult != null ? new
            {
                processed = _lastResult.ProcessedCount,
                added = _lastResult.AddedCount,
                updated = _lastResult.UpdatedCount,
                skipped = _lastResult.SkippedCount,
                errors = _lastResult.ErrorCount
            } : null,
            library = new
            {
                songs = songCount,
                artists = artistCount,
                albums = albumCount
            }
        }));
    }

    // GET /api/v1/admin/system
    [HttpGet("system")]
    public async Task<IActionResult> GetSystemInfo()
    {
        var role = JwtService.GetRole(User);
        if (role != "admin")
            return Ok(ApiResponse<object>.Error(ErrorCodes.Forbidden, "仅管理员可操作"));

        var songCount = await _db.Songs.CountAsync();
        var userCount = await _db.Users.CountAsync();

        var dbFile = new FileInfo(Path.Combine(AppContext.BaseDirectory, "Data", "catclaw.db"));
        var dbSize = dbFile.Exists ? dbFile.Length : 0;

        return Ok(ApiResponse<object>.Ok(new
        {
            server = new
            {
                name = "CatClaw Music Server",
                version = "2.0.0",
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
            },
            database = new
            {
                songs = songCount,
                users = userCount,
                size_bytes = dbSize,
                size_mb = Math.Round(dbSize / 1024.0 / 1024.0, 2)
            },
            system = new
            {
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                processor_count = Environment.ProcessorCount,
                working_set_mb = Math.Round(Environment.WorkingSet / 1024.0 / 1024.0, 1)
            }
        }));
    }

    // POST /api/v1/admin/cleanup
    [HttpPost("cleanup")]
    public async Task<IActionResult> Cleanup()
    {
        var role = JwtService.GetRole(User);
        if (role != "admin")
            return Ok(ApiResponse<object>.Error(ErrorCodes.Forbidden, "仅管理员可操作"));

        // 删除文件不存在的歌曲记录
        var songs = await _db.Songs.ToListAsync();
        int removed = 0;
        foreach (var song in songs)
        {
            if (!System.IO.File.Exists(song.FilePath))
            {
                _db.Songs.Remove(song);
                removed++;
            }
        }
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new { removed }));
    }

    // GET /api/v1/admin/users
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var role = JwtService.GetRole(User);
        if (role != "admin")
            return Ok(ApiResponse<object>.Error(ErrorCodes.Forbidden, "仅管理员可操作"));

        var users = await _db.Users.Select(u => new
        {
            id = u.Id,
            username = u.Username,
            display_name = u.DisplayName,
            role = u.Role,
            created_at = u.CreatedAt,
            last_login_at = u.LastLoginAt
        }).ToListAsync();

        return Ok(ApiResponse<object>.Ok(users));
    }

    // DELETE /api/v1/admin/users/{id}
    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(long id)
    {
        var role = JwtService.GetRole(User);
        if (role != "admin")
            return Ok(ApiResponse<object>.Error(ErrorCodes.Forbidden, "仅管理员可操作"));

        var currentUserId = JwtService.GetUserId(User);
        if (currentUserId == id)
            return Ok(ApiResponse<object>.Error(ErrorCodes.InvalidParameter, "不能删除自己的账户"));

        var user = await _db.Users.FindAsync(id);
        if (user == null)
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "用户不存在"));

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(null!));
    }
}
