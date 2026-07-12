using System.Security.Cryptography;
using System.Text;
using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.Subsonic;

/// <summary>
/// Subsonic API 基础类 — 提供公共辅助方法和响应构建器。
/// 所有 Subsonic partial controllers 继承此类。
/// </summary>
[Route("/rest")]
[ApiController]
public partial class SubsonicController : ControllerBase
{
    protected readonly ApplicationDbContext _db;
    protected readonly ILogger<SubsonicController> _logger;
    protected readonly ServerAuthOptions _authOptions;

    public SubsonicController(
        ApplicationDbContext db,
        ILogger<SubsonicController> logger,
        ServerAuthOptions authOptions)
    {
        _db = db;
        _logger = logger;
        _authOptions = authOptions;
    }

    protected IActionResult SubsonicOk(Dictionary<string, object> payload)
    {
        var wrapper = new Dictionary<string, object>
        {
            ["status"] = "ok",
            ["version"] = "1.16.1",
            ["type"] = "CatClawMusicServer",
            ["serverVersion"] = "2.0.0"
        };
        foreach (var kv in payload) wrapper[kv.Key] = kv.Value;
        return Ok(new Dictionary<string, object> { ["subsonic-response"] = wrapper });
    }

    protected IActionResult SubsonicError(string message, int code)
    {
        var wrapper = new Dictionary<string, object>
        {
            ["status"] = "failed",
            ["version"] = "1.16.1",
            ["type"] = "CatClawMusicServer",
            ["serverVersion"] = "2.0.0",
            ["error"] = new Dictionary<string, object> { ["code"] = code, ["message"] = message }
        };
        return Ok(new Dictionary<string, object> { ["subsonic-response"] = wrapper });
    }

    protected static Dictionary<string, object> SongToDict(Song s) => new()
    {
        ["id"] = s.Id.ToString(),
        ["parent"] = s.AlbumId.ToString(),
        ["isDir"] = false,
        ["title"] = s.Title,
        ["artist"] = s.Artist?.Name ?? "未知艺术家",
        ["artistId"] = s.ArtistId.ToString(),
        ["album"] = s.Album?.Title ?? "未知专辑",
        ["albumId"] = s.AlbumId.ToString(),
        ["duration"] = s.Duration,
        ["bitRate"] = s.Bitrate,
        ["size"] = s.FileSize,
        ["coverArt"] = s.Id.ToString(),
        ["year"] = s.Year,
        ["track"] = s.TrackNumber,
        ["genre"] = s.Genre ?? "",
        ["path"] = s.FilePath,
        ["type"] = "music",
        ["isVideo"] = false,
        ["contentType"] = MimeByExt(Path.GetExtension(s.FilePath))
    };

    protected static Dictionary<string, object> AlbumToDict(Album a) => new()
    {
        ["id"] = a.Id.ToString(),
        ["name"] = a.Title,
        ["artist"] = a.Artist?.Name ?? "未知艺术家",
        ["artistId"] = a.ArtistId.ToString(),
        ["coverArt"] = a.Cover ?? a.Id.ToString(),
        ["songCount"] = a.Songs?.Count ?? 0,
        ["created"] = "2024-01-01T00:00:00Z"
    };

    protected static Dictionary<string, object> ArtistToDict(Artist a) => new()
    {
        ["id"] = a.Id.ToString(),
        ["name"] = a.Name,
        ["coverArt"] = a.Cover ?? "",
        ["albumCount"] = a.Albums?.Count ?? 0
    };

    protected static Dictionary<string, object> PlaylistToDict(Playlist p) => new()
    {
        ["id"] = p.Id.ToString(),
        ["name"] = p.Name,
        ["comment"] = p.Description ?? "",
        ["owner"] = "admin",
        ["public"] = p.IsPublic,
        ["songCount"] = p.Songs?.Count ?? 0,
        ["created"] = p.CreatedAt.ToString("o"),
        ["changed"] = p.UpdatedAt.ToString("o")
    };

    protected static readonly Dictionary<string, string> _mimeByExt = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg", [".mp2"] = "audio/mpeg",
        [".flac"] = "audio/flac", [".wav"] = "audio/wav",
        [".wma"] = "audio/x-ms-wma", [".ogg"] = "audio/ogg",
        [".opus"] = "audio/ogg", [".aiff"] = "audio/aiff", [".aif"] = "audio/aiff",
        [".m4a"] = "audio/mp4", [".mp4"] = "audio/mp4",
        [".ape"] = "audio/x-ape", [".wv"] = "audio/x-wavpack",
        [".mpc"] = "audio/x-musepack", [".tta"] = "audio/x-tta",
    };

    protected static string MimeByExt(string ext)
        => _mimeByExt.TryGetValue(ext, out var m) ? m : "application/octet-stream";

    /// <summary>获取当前请求中的 Subsonic 用户名（用于关联本地用户）</summary>
    protected string? GetSubsonicUser()
        => Request.Query["u"].FirstOrDefault();
}
