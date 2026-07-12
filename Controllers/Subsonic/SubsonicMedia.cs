using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.Subsonic;

public partial class SubsonicController
{
    // GET /rest/stream.view?id=
    [HttpGet("stream.view")]
    public async Task<IActionResult> Stream([FromQuery] string id, [FromQuery] int? maxBitRate, [FromQuery] string? format)
    {
        if (!long.TryParse(id, out var songId)) return BadRequest();

        var song = await _db.Songs.AsNoTracking().FirstOrDefaultAsync(s => s.Id == songId);
        if (song == null || !System.IO.File.Exists(song.FilePath)) return NotFound();

        var contentType = MimeByExt(Path.GetExtension(song.FilePath));
        return PhysicalFile(song.FilePath, contentType, enableRangeProcessing: true);
    }

    // GET /rest/download.view?id=
    [HttpGet("download.view")]
    public async Task<IActionResult> Download([FromQuery] string id)
    {
        if (!long.TryParse(id, out var songId)) return BadRequest();

        var song = await _db.Songs.AsNoTracking().FirstOrDefaultAsync(s => s.Id == songId);
        if (song == null || !System.IO.File.Exists(song.FilePath)) return NotFound();

        var fileName = Path.GetFileName(song.FilePath);
        return PhysicalFile(song.FilePath, "application/octet-stream", fileName);
    }

    // GET /rest/getCoverArt.view?id=
    [HttpGet("getCoverArt.view")]
    public async Task<IActionResult> GetCoverArt([FromQuery] string id, [FromQuery] int? size)
    {
        // Try as song ID
        if (long.TryParse(id, out var songId))
        {
            var song = await _db.Songs.AsNoTracking().FirstOrDefaultAsync(s => s.Id == songId);
            if (song?.CoverArtPath != null && System.IO.File.Exists(song.CoverArtPath))
                return ServeCover(song.CoverArtPath);
        }

        // Try as album ID (al-xxx format or plain number)
        var albumIdStr = id.StartsWith("al-") ? id[3..] : id;
        if (long.TryParse(albumIdStr, out var albumId))
        {
            var album = await _db.Albums.AsNoTracking().FirstOrDefaultAsync(a => a.Id == albumId);
            if (album?.Cover != null && System.IO.File.Exists(album.Cover))
                return ServeCover(album.Cover);
        }

        return NotFound();
    }

    // GET /rest/getLyricsBySongId.view?id=
    [HttpGet("getLyricsBySongId.view")]
    public async Task<IActionResult> GetLyricsBySongId([FromQuery] string id)
    {
        if (!long.TryParse(id, out var songId)) return SubsonicError("Not found", 70);

        var song = await _db.Songs.AsNoTracking().FirstOrDefaultAsync(s => s.Id == songId);
        if (song == null) return SubsonicError("Song not found", 70);

        var text = "";
        if (!string.IsNullOrEmpty(song.LyricsPath) && System.IO.File.Exists(song.LyricsPath))
            text = await System.IO.File.ReadAllTextAsync(song.LyricsPath);

        return SubsonicOk(new Dictionary<string, object>
        {
            ["lyricsBySongId"] = new Dictionary<string, object> { ["value"] = text }
        });
    }

    private IActionResult ServeCover(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var contentType = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
        return PhysicalFile(path, contentType);
    }
}
