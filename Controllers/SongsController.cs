using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SongsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SongsController> _logger;

    public SongsController(ApplicationDbContext db, ILogger<SongsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // GET /api/songs?page=1&page_size=50&artist=&album=
    [HttpGet]
    public async Task<IActionResult> GetSongs(
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 50,
        [FromQuery] string? artist = null,
        [FromQuery] string? album = null)
    {
        if (page < 1) page = 1;
        if (page_size < 1) page_size = 50;
        if (page_size > 200) page_size = 200;

        var query = _db.Songs
            .Include(s => s.Artist)
            .Include(s => s.Album)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(artist))
            query = query.Where(s => s.Artist != null && s.Artist.Name == artist);

        if (!string.IsNullOrWhiteSpace(album))
            query = query.Where(s => s.Album != null && s.Album.Title == album);

        var total = await query.CountAsync();

        var songs = await query
            .OrderBy(s => s.Title)
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
                duration = s.Duration,
                file_size = s.FileSize,
                bitrate = s.Bitrate,
                track_number = s.TrackNumber,
                year = s.Year,
                genre = s.Genre,
                date_added = s.DateAdded,
                cover_art_path = s.CoverArtPath,
                lyrics_path = s.LyricsPath
            })
            .ToListAsync();

        return Ok(new { songs, total });
    }

    // GET /api/songs/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetSong(long id)
    {
        var song = await _db.Songs
            .Include(s => s.Artist)
            .Include(s => s.Album)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id);

        if (song == null) return NotFound();

        return Ok(new
        {
            id = song.Id,
            title = song.Title,
            artist_id = song.ArtistId,
            album_id = song.AlbumId,
            artist = song.Artist != null ? song.Artist.Name : "未知艺术家",
            album = song.Album != null ? song.Album.Title : "未知专辑",
            duration = song.Duration,
            file_path = song.FilePath,
            file_size = song.FileSize,
            bitrate = song.Bitrate,
            track_number = song.TrackNumber,
            year = song.Year,
            genre = song.Genre,
            date_added = song.DateAdded,
            date_modified = song.DateModified,
            cover_art_path = song.CoverArtPath,
            lyrics_path = song.LyricsPath
        });
    }

    // GET /api/songs/{id}/stream
    [HttpGet("{id}/stream")]
    public async Task<IActionResult> StreamSong(long id)
    {
        var song = await _db.Songs.FindAsync(id);
        if (song == null) return NotFound();
        if (!System.IO.File.Exists(song.FilePath)) return NotFound("file not found");

        var fileInfo = new FileInfo(song.FilePath);
        var fileLength = fileInfo.Length;
        var rangeHeader = Request.Headers.Range.ToString();

        Response.Headers.Append("Accept-Ranges", "bytes");

        if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes="))
        {
            // 解析 Range 请求
            var range = rangeHeader["bytes=".Length..].Split('-');
            var start = long.Parse(range[0]);
            var end = range.Length > 1 && !string.IsNullOrEmpty(range[1])
                ? long.Parse(range[1])
                : fileLength - 1;

            var length = end - start + 1;
            Response.StatusCode = 206;
            Response.Headers.Append("Content-Range", $"bytes {start}-{end}/{fileLength}");
            Response.Headers.Append("Content-Length", length.ToString());

            var fs = new FileStream(song.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Seek(start, SeekOrigin.Begin);
            return File(fs, "audio/mpeg", enableRangeProcessing: true);
        }

        Response.Headers.Append("Content-Length", fileLength.ToString());
        return PhysicalFile(song.FilePath, "audio/mpeg", enableRangeProcessing: true);
    }

    // GET /api/songs/{id}/cover
    [HttpGet("{id}/cover")]
    public async Task<IActionResult> GetCover(long id)
    {
        var song = await _db.Songs.FindAsync(id);
        if (song == null) return NotFound();

        var coverPath = song.CoverArtPath;
        if (string.IsNullOrEmpty(coverPath) || !System.IO.File.Exists(coverPath))
            return NotFound();

        var ext = Path.GetExtension(coverPath).ToLowerInvariant();
        var contentType = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };

        return PhysicalFile(coverPath, contentType);
    }

    // GET /api/songs/{id}/lyrics
    [HttpGet("{id}/lyrics")]
    public async Task<IActionResult> GetLyrics(long id)
    {
        var song = await _db.Songs.FindAsync(id);
        if (song == null) return NotFound();
        if (string.IsNullOrEmpty(song.LyricsPath) || !System.IO.File.Exists(song.LyricsPath))
            return NotFound("no lyrics");

        var text = await System.IO.File.ReadAllTextAsync(song.LyricsPath);
        return Content(text, "text/plain; charset=utf-8");
    }
}
