using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using CatClawMusicServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.V1;

[Authorize]
[ApiController]
[Route("api/v1/songs")]
public class SongsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly TranscodingService _transcoding;
    private readonly CoverService _cover;
    private readonly ILogger<SongsController> _logger;

    public SongsController(ApplicationDbContext db, TranscodingService transcoding,
        CoverService cover, ILogger<SongsController> logger)
    {
        _db = db;
        _transcoding = transcoding;
        _cover = cover;
        _logger = logger;
    }

    // GET /api/v1/songs?page=1&page_size=50&artist_id=&album_id=&genre=&sort=&order=
    [HttpGet]
    public async Task<IActionResult> GetSongs(
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 50,
        [FromQuery] long? artist_id = null,
        [FromQuery] long? album_id = null,
        [FromQuery] string? genre = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? order = null)
    {
        page = Math.Max(1, page);
        page_size = Math.Clamp(page_size, 1, 200);

        var query = _db.Songs
            .Include(s => s.Artist)
            .Include(s => s.Album)
            .AsNoTracking()
            .AsQueryable();

        if (artist_id.HasValue)
            query = query.Where(s => s.ArtistId == artist_id.Value);

        if (album_id.HasValue)
            query = query.Where(s => s.AlbumId == album_id.Value);

        if (!string.IsNullOrWhiteSpace(genre))
            query = query.Where(s => s.Genre == genre);

        // 排序
        query = (sort?.ToLower(), order?.ToLower()) switch
        {
            ("title", "desc") => query.OrderByDescending(s => s.Title),
            ("title", _) => query.OrderBy(s => s.Title),
            ("date_added", "asc") => query.OrderBy(s => s.DateAdded),
            ("date_added", _) => query.OrderByDescending(s => s.DateAdded),
            ("year", "desc") => query.OrderByDescending(s => s.Year),
            ("year", _) => query.OrderBy(s => s.Year),
            ("duration", "desc") => query.OrderByDescending(s => s.Duration),
            ("duration", _) => query.OrderBy(s => s.Duration),
            _ => query.OrderBy(s => s.Title)
        };

        var total = await query.CountAsync();

        var songs = await query
            .Skip((page - 1) * page_size)
            .Take(page_size)
            .Select(s => new
            {
                id = s.Id,
                title = s.Title,
                artist_id = s.ArtistId,
                album_id = s.AlbumId,
                artist = s.Artist != null ? s.Artist.Name : "未知艺术家",
                album = s.Album != null ? s.Album.Title : "未知专辑",
                album_cover = s.Album != null ? s.Album.Cover : s.CoverArtPath,
                duration = s.Duration,
                bitrate = s.Bitrate,
                codec = s.Codec,
                is_lossless = s.IsLossless,
                sample_rate = s.SampleRate,
                bit_depth = s.BitDepth,
                channels = s.Channels,
                track_number = s.TrackNumber,
                disc_number = s.DiscNumber,
                year = s.Year,
                genre = s.Genre,
                date_added = s.DateAdded
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            items = songs,
            total,
            page,
            page_size,
            total_pages = (int)Math.Ceiling((double)total / page_size)
        }));
    }

    // GET /api/v1/songs/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetSong(long id)
    {
        var song = await _db.Songs
            .Include(s => s.Artist)
            .Include(s => s.Album)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id);

        if (song == null)
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "歌曲不存在"));

        return Ok(ApiResponse<object>.Ok(new
        {
            id = song.Id,
            title = song.Title,
            artist_id = song.ArtistId,
            album_id = song.AlbumId,
            artist = song.Artist?.Name ?? "未知艺术家",
            album = song.Album?.Title ?? "未知专辑",
            album_cover = song.Album?.Cover ?? song.CoverArtPath,
            duration = song.Duration,
            file_size = song.FileSize,
            bitrate = song.Bitrate,
            codec = song.Codec,
            is_lossless = song.IsLossless,
            sample_rate = song.SampleRate,
            bit_depth = song.BitDepth,
            channels = song.Channels,
            track_number = song.TrackNumber,
            disc_number = song.DiscNumber,
            year = song.Year,
            genre = song.Genre,
            date_added = song.DateAdded,
            date_modified = song.DateModified
        }));
    }

    // GET /api/v1/songs/random?count=50
    [HttpGet("random")]
    public async Task<IActionResult> GetRandomSongs([FromQuery] int count = 20)
    {
        count = Math.Clamp(count, 1, 100);
        var total = await _db.Songs.CountAsync();
        if (total == 0)
            return Ok(ApiResponse<object>.Ok(new { items = Array.Empty<object>() }));

        // 随机取 count 首歌（简单实现：随机跳过）
        var skip = new Random().Next(0, Math.Max(0, total - count));
        var songs = await _db.Songs
            .Include(s => s.Artist)
            .Include(s => s.Album)
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .Skip(skip)
            .Take(count)
            .Select(s => new
            {
                id = s.Id,
                title = s.Title,
                artist = s.Artist != null ? s.Artist.Name : "未知艺术家",
                album = s.Album != null ? s.Album.Title : "未知专辑",
                album_cover = s.Album != null ? s.Album.Cover : s.CoverArtPath,
                duration = s.Duration
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { items = songs }));
    }

    // GET /api/v1/songs/{id}/stream
    [HttpGet("{id}/stream")]
    public async Task<IActionResult> StreamSong(long id, [FromQuery] string? transcode = null, [FromQuery] int? bitrate = null)
    {
        var song = await _db.Songs.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (song == null)
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "歌曲不存在"));

        if (!System.IO.File.Exists(song.FilePath))
            return Ok(ApiResponse<object>.Error(ErrorCodes.FileNotFound, "文件不存在"));

        var contentType = GetContentType(song.FilePath);

        // 如果不需要转码，直接流式传输
        if (string.IsNullOrEmpty(transcode))
        {
            return PhysicalFile(song.FilePath, contentType, enableRangeProcessing: true);
        }

        // 转码流：FFmpeg stdout → Response
        var br = bitrate ?? 256;
        try
        {
            var job = await _transcoding.GetOrStartStreamJobAsync(song.Id, song.FilePath, transcode, br,
                HttpContext.RequestAborted);

            if (job.Process?.StandardOutput.BaseStream == null)
                return Ok(ApiResponse<object>.Error(ErrorCodes.TranscodeError, "转码启动失败"));

            var outCt = transcode switch
            {
                "opus" => "audio/opus",
                "mp3" => "audio/mpeg",
                _ => "audio/aac"
            };

            Response.ContentType = outCt;
            Response.Headers["Transfer-Encoding"] = "chunked";

            await job.Process.StandardOutput.BaseStream.CopyToAsync(Response.Body, HttpContext.RequestAborted);
            return new EmptyResult();
        }
        catch (OperationCanceledException)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "转码流失败: song={SongId} format={Format}", id, transcode);
            return Ok(ApiResponse<object>.Error(ErrorCodes.TranscodeError, "转码失败"));
        }
    }

    // GET /api/v1/songs/{id}/cover?size=small|medium|large|original
    [HttpGet("{id}/cover")]
    public async Task<IActionResult> GetCover(long id, [FromQuery] string? size = null)
    {
        var song = await _db.Songs.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (song == null)
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "歌曲不存在"));

        var coverPath = _cover.GetCoverPath(song.CoverArtPath, size ?? "original");
        if (coverPath == null || !System.IO.File.Exists(coverPath))
            return NotFound();

        var ext = Path.GetExtension(coverPath).ToLowerInvariant();
        var ct = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };

        return PhysicalFile(coverPath, ct);
    }

    // GET /api/v1/songs/{id}/lyrics
    [HttpGet("{id}/lyrics")]
    public async Task<IActionResult> GetLyrics(long id)
    {
        var song = await _db.Songs.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (song == null)
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "歌曲不存在"));

        if (string.IsNullOrEmpty(song.LyricsPath) || !System.IO.File.Exists(song.LyricsPath))
            return Ok(ApiResponse<object>.Ok(new { type = "plain", synced = false, content = "", lines = Array.Empty<object>() }));

        var text = await System.IO.File.ReadAllTextAsync(song.LyricsPath);
        var ext = Path.GetExtension(song.LyricsPath);
        var result = LyricsParser.Parse(text, ext);

        return Ok(ApiResponse<object>.Ok(new
        {
            type = result.Type,
            synced = result.Synced,
            content = result.Content,
            translation = result.Translation,
            lines = result.Lines.Select(l => new
            {
                start_time_ms = l.StartTimeMs,
                text = l.Text,
                words = l.Words?.Select(w => new { start_time_ms = w.StartTimeMs, text = w.Text }).ToList()
            }).ToList()
        }));
    }

    // GET /api/v1/songs/{id}/download
    [HttpGet("{id}/download")]
    public async Task<IActionResult> DownloadSong(long id)
    {
        var song = await _db.Songs.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (song == null)
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "歌曲不存在"));
        if (!System.IO.File.Exists(song.FilePath))
            return Ok(ApiResponse<object>.Error(ErrorCodes.FileNotFound, "文件不存在"));

        var fileName = Path.GetFileName(song.FilePath);
        return PhysicalFile(song.FilePath, "application/octet-stream", fileName);
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
            ".wma" => "audio/x-ms-wma",
            ".ape" => "audio/x-ape",
            ".aiff" or ".aif" => "audio/aiff",
            _ => "application/octet-stream"
        };
    }
}
