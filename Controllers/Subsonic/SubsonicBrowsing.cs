using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.Subsonic;

public partial class SubsonicController
{
    // GET /rest/getMusicFolders.view
    [HttpGet("getMusicFolders.view")]
    public IActionResult GetMusicFolders()
    {
        return SubsonicOk(new Dictionary<string, object>
        {
            ["musicFolders"] = new Dictionary<string, object>
            {
                ["musicFolder"] = new[]
                {
                    new Dictionary<string, object> { ["id"] = "1", ["name"] = "Music" }
                }
            }
        });
    }

    // GET /rest/getIndexes.view
    [HttpGet("getIndexes.view")]
    public async Task<IActionResult> GetIndexes()
    {
        var artists = await _db.Artists
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .ToListAsync();

        var indexes = artists
            .GroupBy(a => string.IsNullOrEmpty(a.Name) ? "#" : char.ToUpper(a.Name[0]).ToString())
            .OrderBy(g => g.Key == "#" ? 1 : 0)
            .ThenBy(g => g.Key)
            .Select(g => new Dictionary<string, object>
            {
                ["name"] = g.Key,
                ["artist"] = g.Select(a => ArtistToDict(a)).ToArray()
            }).ToArray();

        return SubsonicOk(new Dictionary<string, object>
        {
            ["indexes"] = new Dictionary<string, object>
            {
                ["lastModified"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["ignoredArticles"] = "The El La Los Las Le Les",
                ["index"] = indexes
            }
        });
    }

    // GET /rest/getArtists.view
    [HttpGet("getArtists.view")]
    public async Task<IActionResult> GetArtists()
    {
        var artists = await _db.Artists
            .Include(a => a.Albums)
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .ToListAsync();

        var indexes = artists
            .GroupBy(a => string.IsNullOrEmpty(a.Name) ? "#" : char.ToUpper(a.Name[0]).ToString())
            .OrderBy(g => g.Key == "#" ? 1 : 0)
            .ThenBy(g => g.Key)
            .Select(g => new Dictionary<string, object>
            {
                ["name"] = g.Key,
                ["artist"] = g.Select(a => ArtistToDict(a)).ToArray()
            }).ToArray();

        return SubsonicOk(new Dictionary<string, object>
        {
            ["artists"] = new Dictionary<string, object>
            {
                ["ignoredArticles"] = "The El La Los Las Le Les",
                ["index"] = indexes
            }
        });
    }

    // GET /rest/getArtist.view?id=
    [HttpGet("getArtist.view")]
    public async Task<IActionResult> GetArtist([FromQuery] string id)
    {
        if (!long.TryParse(id, out var artistId)) return SubsonicError("Not found", 70);

        var artist = await _db.Artists
            .Include(a => a.Albums)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == artistId);

        if (artist == null) return SubsonicError("Artist not found", 70);

        var result = ArtistToDict(artist);
        result["album"] = (artist.Albums ?? new List<Album>())
            .Select(a => AlbumToDict(a)).ToArray();

        return SubsonicOk(new Dictionary<string, object> { ["artist"] = result });
    }

    // GET /rest/getMusicDirectory.view?id=
    [HttpGet("getMusicDirectory.view")]
    public async Task<IActionResult> GetMusicDirectory([FromQuery] string id)
    {
        if (!long.TryParse(id, out var dirId)) return SubsonicError("Not found", 70);

        // Try as artist first
        var artist = await _db.Artists
            .Include(a => a.Albums)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == dirId);

        if (artist != null)
        {
            var children = (artist.Albums ?? new List<Album>())
                .Select(a => AlbumToDict(a)).ToArray();

            return SubsonicOk(new Dictionary<string, object>
            {
                ["directory"] = new Dictionary<string, object>
                {
                    ["id"] = artist.Id.ToString(),
                    ["name"] = artist.Name,
                    ["child"] = children
                }
            });
        }

        // Try as album
        var album = await _db.Albums
            .Include(a => a.Artist)
            .Include(a => a.Songs!).ThenInclude(s => s.Artist)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == dirId);

        if (album != null)
        {
            var children = (album.Songs ?? new List<Song>())
                .OrderBy(s => s.TrackNumber)
                .Select(s => SongToDict(s)).ToArray();

            return SubsonicOk(new Dictionary<string, object>
            {
                ["directory"] = new Dictionary<string, object>
                {
                    ["id"] = album.Id.ToString(),
                    ["parent"] = album.ArtistId.ToString(),
                    ["name"] = album.Title,
                    ["child"] = children
                }
            });
        }

        return SubsonicError("Not found", 70);
    }

    // GET /rest/getGenres.view
    [HttpGet("getGenres.view")]
    public async Task<IActionResult> GetGenres()
    {
        var genreData = await _db.Songs
            .AsNoTracking()
            .Where(s => !string.IsNullOrEmpty(s.Genre))
            .GroupBy(s => s.Genre!)
            .Select(g => new
            {
                Value = g.Key,
                SongCount = g.Count(),
                AlbumCount = g.Select(s => s.AlbumId).Distinct().Count()
            })
            .ToListAsync();

        var genres = genreData.Select(g => new Dictionary<string, object>
        {
            ["value"] = g.Value,
            ["songCount"] = g.SongCount,
            ["albumCount"] = g.AlbumCount
        }).ToArray();

        return SubsonicOk(new Dictionary<string, object>
        {
            ["genres"] = new Dictionary<string, object> { ["genre"] = genres }
        });
    }

    // GET /rest/getSongsByGenre.view?genre=
    [HttpGet("getSongsByGenre.view")]
    public async Task<IActionResult> GetSongsByGenre(
        [FromQuery] string genre = "",
        [FromQuery] int count = 10,
        [FromQuery] int offset = 0)
    {
        count = Math.Clamp(count, 1, 500);

        var songs = await _db.Songs
            .Include(s => s.Artist).Include(s => s.Album)
            .AsNoTracking()
            .Where(s => s.Genre == genre)
            .OrderBy(s => s.Title)
            .Skip(offset).Take(count)
            .ToListAsync();

        return SubsonicOk(new Dictionary<string, object>
        {
            ["songsByGenre"] = new Dictionary<string, object>
            {
                ["song"] = songs.Select(SongToDict).ToArray()
            }
        });
    }
}
