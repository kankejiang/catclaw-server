using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using CatClawMusicServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ScanController : ControllerBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ScannerOptions _options;
    private readonly ILogger<ScanController> _logger;

    private static ScanStatus _status = new();

    public ScanController(
        IServiceScopeFactory scopeFactory,
        ScannerOptions options,
        ILogger<ScanController> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    // POST /api/scan
    [HttpPost]
    public IActionResult StartScan()
    {
        if (_status.Running)
            return BadRequest(new { message = "扫描正在进行中", progress = _status });

        _status = new ScanStatus
        {
            Running = true,
            StartedAt = DateTime.UtcNow,
            ProcessedCount = 0,
            AddedCount = 0,
            UpdatedCount = 0,
            ErrorCount = 0
        };

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scanner = scope.ServiceProvider.GetRequiredService<MusicScanner>();

                var result = await scanner.ScanDirectoryAsync(
                    _options.MusicDirectory,
                    _options.CoverOutputDir,
                    CancellationToken.None);

                _status.ProcessedCount = result.ProcessedCount;
                _status.AddedCount = result.AddedCount;
                _status.UpdatedCount = result.UpdatedCount;
                _status.SkippedCount = result.SkippedCount;
                _status.ErrorCount = result.ErrorCount;
                _status.CompletedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "扫描失败");
                _status.ErrorMessage = ex.Message;
            }
            finally
            {
                _status.Running = false;
            }
        });

        return Ok(new { message = "扫描已启动", progress = _status });
    }

    // GET /api/scan/status
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(_status);
    }
}

public class ScanStatus
{
    public bool Running { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int ProcessedCount { get; set; }
    public int AddedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int ErrorCount { get; set; }
    public string? ErrorMessage { get; set; }
}
