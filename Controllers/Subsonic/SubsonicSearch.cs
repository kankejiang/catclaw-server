using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.Subsonic;

public partial class SubsonicController
{
    // GET /rest/search2.view
    [HttpGet("search2.view")]
    public async Task<IActionResult> Search2(
        [FromQuery] string query = "",
        [FromQuery] int songCount = 20, [FromQuery] int songOffset = 0,
        [FromQuery] int albumCount = 20, [FromQuery] int albumOffset = 0,
        [FromQuery] int artistCount = 20, [FromQuery] int artistOffset = 0)
    {
        return await DoSearch(query, songCount, songOffset, albumCount, albumOffset, artistCount, artistOffset, v3: false);
    }

    // GET /rest/search3.view
    [HttpGet("search3.view")]
    public async Task<IActionResult> Search3(
        [FromQuery] string query = "",
        [FromQuery] int songCount = 20, [FromQuery] int songOffset = 0,
        [FromQuery] int albumCount = 20, [FromQuery] int albumOffset = 0,
        [FromQuery] int artistCount = 20, [FromQuery] int artistOffset = 0)
    {
        return await DoSearch(query, songCount, songOffset, albumCount, albumOffset, artistCount, artistOffset, v3: true);
    }

    private async Task<IActionResult> DoSearch(string query,
        int songCount, int songOffset,
        int albumCount, int albumOffset,
        int artistCount, int artistOffset,
        bool v3)
    {
        songCount = Math.Clamp(songCount, 1, 200);
        albumCount = Math.Clamp(albumCount, 1, 200);
        artistCount = Math.Clamp(artistCount, 1, 200);
        var q = (query ?? "").Trim();

        // 并发搜索三个维度（参考 Navidrome 模式）
        var songsTask = SearchSongs(q, songCount, songOffset);
        var albumsTask = SearchAlbums(q, albumCount, albumOffset);
        var artistsTask = SearchArtists(q, artistCount, artistOffset);

        await Task.WhenAll(songsTask, albumsTask, artistsTask);

        var songs = await songsTask;
        var albums = await albumsTask;
        var artists = await artistsTask;

        if (v3)
        {
            return SubsonicOk(new Dictionary<string, object>
            {
                ["searchResult3"] = new Dictionary<string, object>
                {
                    ["song"] = songs.Select(SongToDict).ToArray(),
                    ["album"] = albums.Select(AlbumToDict).ToArray(),
                    ["artist"] = artists.Select(ArtistToDict).ToArray()
                }
            });
        }
        else
        {
            return SubsonicOk(new Dictionary<string, object>
            {
                ["searchResult2"] = new Dictionary<string, object>
                {
                    ["song"] = songs.Select(SongToDict).ToArray(),
                    ["album"] = albums.Select(AlbumToDict).ToArray(),
                    ["artist"] = artists.Select(ArtistToDict).ToArray()
                }
            });
        }
    }

    private async Task<List<Song>> SearchSongs(string q, int count, int offset)
    {
        IQueryable<Song> query = _db.Songs.Include(s => s.Artist).Include(s => s.Album).AsNoTracking();
        query = string.IsNullOrEmpty(q)
            ? query.OrderBy(s => s.Title)
            : query.Where(s => s.Title.Contains(q)
                || (s.Artist != null && s.Artist.Name.Contains(q))
                || (s.Album != null && s.Album.Title.Contains(q)));

        return await query.Skip(offset).Take(count).ToListAsync();
    }

    private async Task<List<Album>> SearchAlbums(string q, int count, int offset)
    {
        IQueryable<Album> query = _db.Albums.Include(a => a.Artist).Include(a => a.Songs).AsNoTracking();
        query = string.IsNullOrEmpty(q)
            ? query.OrderBy(a => a.Title)
            : query.Where(a => a.Title.Contains(q)
                || (a.Artist != null && a.Artist.Name.Contains(q)));

        return await query.Skip(offset).Take(count).ToListAsync();
    }

    private async Task<List<Artist>> SearchArtists(string q, int count, int offset)
    {
        IQueryable<Artist> query = _db.Artists.Include(a => a.Albums).AsNoTracking();
        query = string.IsNullOrEmpty(q)
            ? query.OrderBy(a => a.Name)
            : query.Where(a => a.Name.Contains(q));

        return await query.Skip(offset).Take(count).ToListAsync();
    }
}
