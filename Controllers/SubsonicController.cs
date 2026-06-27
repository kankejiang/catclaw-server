using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers;

/// <summary>
/// Subsonic / Navidrome 兼容端点（/rest/*.view）。
/// 让猫爪音乐客户端（及任意标准 Subsonic 客户端）以 Navidrome 协议直连 NAS 媒体中心。
/// 认证：Subsonic token（t = md5(password + salt)），password 即服务端 AccessToken。
/// </summary>
[Route("/rest")]
[ApiController]
public class SubsonicController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public SubsonicController(ApplicationDbContext db) => _db = db;

    private IActionResult Subsonic(Dictionary<string, object> payload)
    {
        var wrapper = new Dictionary<string, object>
        {
            ["status"] = "ok",
            ["version"] = "1.16.1",
            ["type"] = "CatClawMusicServer",
            ["serverVersion"] = "1.0.0"
        };
        foreach (var kv in payload) wrapper[kv.Key] = kv.Value;
        return Ok(new Dictionary<string, object> { ["subsonic-response"] = wrapper });
    }

    private IActionResult SubsonicError(string message, int code)
    {
        var wrapper = new Dictionary<string, object>
        {
            ["status"] = "failed",
            ["version"] = "1.16.1",
            ["type"] = "CatClawMusicServer",
            ["serverVersion"] = "1.0.0",
            ["error"] = new Dictionary<string, object> { ["code"] = code, ["message"] = message }
        };
        return Ok(new Dictionary<string, object> { ["subsonic-response"] = wrapper });
    }

    private static Dictionary<string, object> SongToDict(Song s) => new()
    {
        ["id"] = s.Id.ToString(),
        ["title"] = s.Title,
        ["artist"] = s.Artist?.Name ?? "未知艺术家",
        ["album"] = s.Album?.Title ?? "未知专辑",
        ["duration"] = s.Duration,
        ["bitRate"] = s.Bitrate,
        ["size"] = s.FileSize,
        ["coverArt"] = s.Id.ToString(),
        ["year"] = s.Year,
        ["track"] = s.TrackNumber,
        ["genre"] = s.Genre,
        ["path"] = s.FilePath
    };

    // GET /rest/ping.view
    [HttpGet("ping.view")]
    public IActionResult Ping() => Subsonic(new Dictionary<string, object>());

    // GET /rest/getAlbumList2.view?type=&size=&offset=
    [HttpGet("getAlbumList2.view")]
    public async Task<IActionResult> GetAlbumList2(
        [FromQuery] string type = "alphabeticalByArtist",
        [FromQuery] int size = 200,
        [FromQuery] int offset = 0)
    {
        if (size < 1) size = 200;
        if (size > 500) size = 500;

        IQueryable<Album> query = _db.Albums.Include(a => a.Artist);
        query = type switch
        {
            "newest" or "byYear" => query.OrderByDescending(a => a.Id),
            "byGenre" => query.OrderBy(a => a.Title),
            _ => query.OrderBy(a => a.Artist != null ? a.Artist.Name : "").ThenBy(a => a.Title)
        };

        var albums = await query.Skip(offset).Take(size).AsNoTracking().ToListAsync();
        var arr = albums.Select(a => new Dictionary<string, object>
        {
            ["id"] = a.Id.ToString(),
            ["name"] = a.Title,
            ["artist"] = a.Artist?.Name ?? "未知艺术家",
            ["coverArt"] = a.Id.ToString(),
            ["year"] = 0,
            ["songCount"] = 0
        }).ToArray();

        return Subsonic(new Dictionary<string, object>
        {
            ["albumList2"] = new Dictionary<string, object> { ["album"] = arr }
        });
    }

    // GET /rest/getAlbum.view?id=
    [HttpGet("getAlbum.view")]
    public async Task<IActionResult> GetAlbum([FromQuery] string id)
    {
        if (!long.TryParse(id, out var albumId)) return SubsonicError("Not found", 70);
        var album = await _db.Albums
            .Include(a => a.Artist)
            .Include(a => a.Songs).ThenInclude(s => s.Artist)
            .FirstOrDefaultAsync(a => a.Id == albumId);
        if (album == null) return SubsonicError("Not found", 70);

        var songs = (album.Songs ?? new List<Song>())
            .OrderBy(s => s.TrackNumber)
            .Select(SongToDict).ToArray();

        var coverArt = songs.Length > 0 ? songs[0]["coverArt"] : album.Id.ToString();
        var albumDict = new Dictionary<string, object>
        {
            ["id"] = album.Id.ToString(),
            ["name"] = album.Title,
            ["artist"] = album.Artist?.Name ?? "未知艺术家",
            ["coverArt"] = coverArt,
            ["songCount"] = songs.Length,
            ["song"] = songs
        };
        return Subsonic(new Dictionary<string, object> { ["album"] = albumDict });
    }

    // GET /rest/getSong.view?id=
    [HttpGet("getSong.view")]
    public async Task<IActionResult> GetSong([FromQuery] string id)
    {
        if (!long.TryParse(id, out var songId)) return SubsonicError("Not found", 70);
        var song = await _db.Songs.Include(s => s.Artist).Include(s => s.Album)
            .FirstOrDefaultAsync(s => s.Id == songId);
        if (song == null) return SubsonicError("Not found", 70);
        return Subsonic(new Dictionary<string, object> { ["song"] = SongToDict(song) });
    }

    // GET /rest/search3.view?query=&songCount=
    [HttpGet("search3.view")]
    public async Task<IActionResult> Search3([FromQuery] string query = "", [FromQuery] int songCount = 50)
    {
        if (songCount < 1) songCount = 50;
        if (songCount > 200) songCount = 200;
        var q = (query ?? "").Trim();

        List<Song> songs;
        if (string.IsNullOrEmpty(q))
            songs = await _db.Songs.Include(s => s.Artist).Include(s => s.Album)
                .OrderBy(s => s.Title).Take(songCount).AsNoTracking().ToListAsync();
        else
            songs = await _db.Songs.Include(s => s.Artist).Include(s => s.Album)
                .Where(s => s.Title.Contains(q)
                         || (s.Artist != null && s.Artist.Name.Contains(q))
                         || (s.Album != null && s.Album.Title.Contains(q)))
                .OrderBy(s => s.Title).Take(songCount).AsNoTracking().ToListAsync();

        var arr = songs.Select(SongToDict).ToArray();
        return Subsonic(new Dictionary<string, object>
        {
            ["searchResult3"] = new Dictionary<string, object> { ["song"] = arr }
        });
    }

    // GET /rest/stream.view?id=
    [HttpGet("stream.view")]
    public async Task<IActionResult> Stream([FromQuery] string id)
    {
        if (!long.TryParse(id, out var songId)) return BadRequest();
        var song = await _db.Songs.FindAsync(songId);
        if (song == null || !System.IO.File.Exists(song.FilePath)) return NotFound();

        var fileLength = new System.IO.FileInfo(song.FilePath).Length;
        var rangeHeader = Request.Headers.Range.ToString();
        Response.Headers.Append("Accept-Ranges", "bytes");

        if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes="))
        {
            var range = rangeHeader["bytes=".Length..].Split('-');
            var start = long.Parse(range[0]);
            var end = range.Length > 1 && !string.IsNullOrEmpty(range[1])
                ? long.Parse(range[1]) : fileLength - 1;
            var length = end - start + 1;
            Response.StatusCode = 206;
            Response.Headers.Append("Content-Range", $"bytes {start}-{end}/{fileLength}");
            Response.Headers.Append("Content-Length", length.ToString());
            var fs = new FileStream(song.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Seek(start, SeekOrigin.Begin);
            return File(fs, ContentTypeFor(song.FilePath), enableRangeProcessing: true);
        }

        Response.Headers.Append("Content-Length", fileLength.ToString());
        return PhysicalFile(song.FilePath, ContentTypeFor(song.FilePath), enableRangeProcessing: true);
    }

    private static readonly Dictionary<string, string> MimeByExt = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg", [".mp2"] = "audio/mpeg",
        [".flac"] = "audio/flac", [".wav"] = "audio/wav",
        [".wma"] = "audio/x-ms-wma", [".ogg"] = "audio/ogg",
        [".opus"] = "audio/ogg", [".aiff"] = "audio/aiff",
        [".m4a"] = "audio/mp4", [".mp4"] = "audio/mp4",
        [".ape"] = "audio/x-ape", [".wv"] = "audio/x-wavpack",
        [".mpc"] = "audio/x-musepack", [".tta"] = "audio/x-ttaster",
    };
    private static string ContentTypeFor(string path)
        => MimeByExt.TryGetValue(System.IO.Path.GetExtension(path), out var m) ? m : "application/octet-stream";

    // GET /rest/getCoverArt.view?id=
    [HttpGet("getCoverArt.view")]
    public async Task<IActionResult> GetCoverArt([FromQuery] string id)
    {
        if (!long.TryParse(id, out var songId)) return BadRequest();
        var song = await _db.Songs.FindAsync(songId);
        var coverPath = song?.CoverArtPath;
        if (string.IsNullOrEmpty(coverPath) || !System.IO.File.Exists(coverPath)) return NotFound();

        var ext = System.IO.Path.GetExtension(coverPath).ToLowerInvariant();
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

    // GET /rest/getLyricsBySongId.view?id=
    [HttpGet("getLyricsBySongId.view")]
    public async Task<IActionResult> GetLyricsBySongId([FromQuery] string id)
    {
        if (!long.TryParse(id, out var songId)) return SubsonicError("Not found", 70);
        var song = await _db.Songs.FindAsync(songId);
        if (song == null) return SubsonicError("Not found", 70);
        var text = (!string.IsNullOrEmpty(song.LyricsPath) && System.IO.File.Exists(song.LyricsPath))
            ? await System.IO.File.ReadAllTextAsync(song.LyricsPath)
            : "";
        return Subsonic(new Dictionary<string, object>
        {
            ["lyricsBySongId"] = new Dictionary<string, object> { ["value"] = text }
        });
    }
}
