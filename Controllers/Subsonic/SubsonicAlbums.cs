using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.Subsonic;

public partial class SubsonicController
{
    // GET /rest/getAlbumList2.view
    [HttpGet("getAlbumList2.view")]
    public async Task<IActionResult> GetAlbumList2(
        [FromQuery] string type = "alphabeticalByArtist",
        [FromQuery] int size = 200,
        [FromQuery] int offset = 0)
    {
        size = Math.Clamp(size, 1, 500);

        IQueryable<Album> query = _db.Albums.Include(a => a.Artist).Include(a => a.Songs).AsNoTracking();
        query = type switch
        {
            "newest" => query.OrderByDescending(a => a.Id),
            "frequent" or "recent" => query.OrderByDescending(a => a.Id), // simplified
            "byYear" => query.OrderBy(a => a.Title),
            "alphabeticalByArtist" => query.OrderBy(a => a.Artist!.Name).ThenBy(a => a.Title),
            _ => query.OrderBy(a => a.Title)
        };

        var albums = await query.Skip(offset).Take(size).ToListAsync();

        return SubsonicOk(new Dictionary<string, object>
        {
            ["albumList2"] = new Dictionary<string, object>
            {
                ["album"] = albums.Select(AlbumToDict).ToArray()
            }
        });
    }

    // GET /rest/getAlbumList.view (legacy alias)
    [HttpGet("getAlbumList.view")]
    public Task<IActionResult> GetAlbumList(
        [FromQuery] string type = "alphabeticalByArtist",
        [FromQuery] int size = 200,
        [FromQuery] int offset = 0)
        => GetAlbumList2(type, size, offset);

    // GET /rest/getAlbum.view?id=
    [HttpGet("getAlbum.view")]
    public async Task<IActionResult> GetAlbum([FromQuery] string id)
    {
        if (!long.TryParse(id, out var albumId)) return SubsonicError("Not found", 70);

        var album = await _db.Albums
            .Include(a => a.Artist)
            .Include(a => a.Songs!).ThenInclude(s => s.Artist)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == albumId);

        if (album == null) return SubsonicError("Album not found", 70);

        var songs = (album.Songs ?? new List<Song>())
            .OrderBy(s => s.DiscNumber).ThenBy(s => s.TrackNumber)
            .Select(SongToDict).ToArray();

        var albumDict = AlbumToDict(album);
        albumDict["song"] = songs;

        return SubsonicOk(new Dictionary<string, object> { ["album"] = albumDict });
    }

    // GET /rest/getSong.view?id=
    [HttpGet("getSong.view")]
    public async Task<IActionResult> GetSong([FromQuery] string id)
    {
        if (!long.TryParse(id, out var songId)) return SubsonicError("Not found", 70);

        var song = await _db.Songs
            .Include(s => s.Artist).Include(s => s.Album)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == songId);

        if (song == null) return SubsonicError("Song not found", 70);
        return SubsonicOk(new Dictionary<string, object> { ["song"] = SongToDict(song) });
    }

    // GET /rest/getRandomSongs.view
    [HttpGet("getRandomSongs.view")]
    public async Task<IActionResult> GetRandomSongs([FromQuery] int size = 10)
    {
        size = Math.Clamp(size, 1, 500);
        var total = await _db.Songs.CountAsync();
        if (total == 0) return SubsonicOk(new Dictionary<string, object>
        {
            ["randomSongs"] = new Dictionary<string, object> { ["song"] = Array.Empty<object>() }
        });

        var skip = new Random().Next(0, Math.Max(0, total - size));
        var songs = await _db.Songs
            .Include(s => s.Artist).Include(s => s.Album)
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .Skip(skip).Take(size)
            .ToListAsync();

        return SubsonicOk(new Dictionary<string, object>
        {
            ["randomSongs"] = new Dictionary<string, object> { ["song"] = songs.Select(SongToDict).ToArray() }
        });
    }

    // GET /rest/getStarred2.view
    [HttpGet("getStarred2.view")]
    public async Task<IActionResult> GetStarred2()
    {
        var user = await ResolveSubsonicUser();
        if (user == null) return SubsonicError("Unauthorized", 40);

        var favorites = await _db.Favorites
            .Where(f => f.UserId == user.Id)
            .Include(f => f.Song!).ThenInclude(s => s.Artist)
            .Include(f => f.Song!).ThenInclude(s => s.Album)
            .AsNoTracking()
            .ToListAsync();

        return SubsonicOk(new Dictionary<string, object>
        {
            ["starred2"] = new Dictionary<string, object>
            {
                ["song"] = favorites.Select(f => SongToDict(f.Song!)).ToArray(),
                ["album"] = Array.Empty<object>(),
                ["artist"] = Array.Empty<object>()
            }
        });
    }

    // GET /rest/getNowPlaying.view
    [HttpGet("getNowPlaying.view")]
    public IActionResult GetNowPlaying()
    {
        // Simplified: return empty (could be enhanced with real-time tracking)
        return SubsonicOk(new Dictionary<string, object>
        {
            ["nowPlaying"] = new Dictionary<string, object> { ["entry"] = Array.Empty<object>() }
        });
    }

    private async Task<User?> ResolveSubsonicUser()
    {
        var username = GetSubsonicUser();
        if (string.IsNullOrEmpty(username)) return null;
        return await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
    }
}
