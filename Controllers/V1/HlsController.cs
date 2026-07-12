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
[Route("api/v1/hls")]
public class HlsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly TranscodingService _transcoding;
    private readonly StreamingOptions _opts;
    private readonly ILogger<HlsController> _logger;

    public HlsController(ApplicationDbContext db, TranscodingService transcoding,
        StreamingOptions opts, ILogger<HlsController> logger)
    {
        _db = db;
        _transcoding = transcoding;
        _opts = opts;
        _logger = logger;
    }

    // GET /api/v1/hls/{songId}/master.m3u8
    [HttpGet("{songId}/master.m3u8")]
    public async Task<IActionResult> GetMasterPlaylist(long songId)
    {
        if (!_opts.HlsEnabled)
            return Ok(ApiResponse<object>.Error(ErrorCodes.TranscodeError, "HLS 未启用"));

        var song = await _db.Songs.AsNoTracking().FirstOrDefaultAsync(s => s.Id == songId);
        if (song == null)
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "歌曲不存在"));

        if (!System.IO.File.Exists(song.FilePath))
            return Ok(ApiResponse<object>.Error(ErrorCodes.FileNotFound, "文件不存在"));

        var playlist = _transcoding.BuildMasterPlaylist(song.Bitrate, song.IsLossless, _opts.DefaultBitrates);

        return Content(playlist, "application/vnd.apple.mpegurl");
    }

    // GET /api/v1/hls/{songId}/{bitrate}/index.m3u8
    [HttpGet("{songId}/{bitrate}/index.m3u8")]
    public async Task<IActionResult> GetSegmentPlaylist(long songId, int bitrate)
    {
        if (!_opts.HlsEnabled)
            return Ok(ApiResponse<object>.Error(ErrorCodes.TranscodeError, "HLS 未启用"));

        var song = await _db.Songs.AsNoTracking().FirstOrDefaultAsync(s => s.Id == songId);
        if (song == null)
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "歌曲不存在"));

        if (!System.IO.File.Exists(song.FilePath))
            return Ok(ApiResponse<object>.Error(ErrorCodes.FileNotFound, "文件不存在"));

        // 无损直出
        if (bitrate == 0 || (bitrate.ToString() == "original"))
        {
            var playlist = _transcoding.BuildOriginalPlaylist(song.FilePath, song.Duration);
            return Content(playlist, "application/vnd.apple.mpegurl");
        }

        // 获取或启动转码
        var job = await _transcoding.GetOrStartHlsJobAsync(songId, song.FilePath, bitrate,
            HttpContext.RequestAborted);

        // 等待至少 3 个分片就绪
        var ready = await _transcoding.WaitForSegmentsAsync(job.OutputDir, 3, HttpContext.RequestAborted);
        if (!ready && job.Status != TranscodeStatus.Complete)
            return StatusCode(408, "转码超时，请稍后重试");

        // 读取并返回 index.m3u8
        var indexFile = Path.Combine(job.OutputDir, "index.m3u8");
        if (!System.IO.File.Exists(indexFile))
            return NotFound();

        var content = await System.IO.File.ReadAllTextAsync(indexFile);

        // 替换分片路径为相对 URL
        content = content.Replace(job.OutputDir + Path.DirectorySeparatorChar, "");
        content = content.Replace(job.OutputDir + "/", "");

        return Content(content, "application/vnd.apple.mpegurl");
    }

    // GET /api/v1/hls/{songId}/{bitrate}/{segment}
    [HttpGet("{songId}/{bitrate}/{segment}")]
    public async Task<IActionResult> GetSegment(long songId, int bitrate, string segment)
    {
        var song = await _db.Songs.AsNoTracking().FirstOrDefaultAsync(s => s.Id == songId);
        if (song == null) return NotFound();

        // 无损直出
        if (segment == "original")
        {
            if (!System.IO.File.Exists(song.FilePath)) return NotFound();
            var ct = GetContentType(song.FilePath);
            return PhysicalFile(song.FilePath, ct, enableRangeProcessing: true);
        }

        var segmentPath = Path.Combine(_opts.TranscodeDir, songId.ToString(), bitrate.ToString(), segment);
        if (!System.IO.File.Exists(segmentPath))
        {
            // 可能分片还未生成，等一下
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 5000 && !System.IO.File.Exists(segmentPath))
            {
                await Task.Delay(100);
            }
            if (!System.IO.File.Exists(segmentPath))
                return NotFound();
        }

        return PhysicalFile(segmentPath, "audio/aac");
    }

    private static string GetContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".mp3" => "audio/mpeg",
            ".flac" => "audio/flac",
            ".wav" => "audio/wav",
            ".m4a" or ".mp4" => "audio/mp4",
            ".ogg" => "audio/ogg",
            ".opus" => "audio/opus",
            _ => "application/octet-stream"
        };
    }
}
