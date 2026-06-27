using CatClawMusicServer.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AlbumsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public AlbumsController(ApplicationDbContext db)
    {
        _db = db;
    }

    // GET /api/albums?page=1&page_size=50&artist=
    [HttpGet]
    public async Task<IActionResult> GetAlbums(
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 50,
        [FromQuery] string? artist = null)
    {
        if (page < 1) page = 1;
        if (page_size < 1) page_size = 50;
        if (page_size > 200) page_size = 200;

        var query = _db.Albums
            .Include(a => a.Artist)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(artist))
            query = query.Where(a => a.Artist != null && a.Artist.Name == artist);

        var total = await query.CountAsync();

        var albums = await query
            .OrderBy(a => a.Title)
            .Skip((page - 1) * page_size)
            .Take(page_size)
            .Select(a => new
            {
                id = a.Id,
                title = a.Title,
                artist_id = a.ArtistId,
                artist = a.Artist != null ? a.Artist.Name : "未知艺术家",
                cover = a.Cover,
                song_count = _db.Songs.Count(s => s.AlbumId == a.Id)
            })
            .ToListAsync();

        return Ok(new { albums, total });
    }

    // GET /api/albums/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetAlbum(long id)
    {
        var album = await _db.Albums
            .Include(a => a.Artist)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id);

        if (album == null) return NotFound();

        var songCount = await _db.Songs.CountAsync(s => s.AlbumId == id);

        return Ok(new
        {
            id = album.Id,
            title = album.Title,
            artist_id = album.ArtistId,
            artist = album.Artist != null ? album.Artist.Name : "未知艺术家",
            cover = album.Cover,
            song_count = songCount
        });
    }

    // GET /api/albums/{id}/songs?page=1&page_size=50
    [HttpGet("{id}/songs")]
    public async Task<IActionResult> GetAlbumSongs(
        long id,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 50)
    {
        if (page < 1) page = 1;
        if (page_size < 1) page_size = 50;

        var albumExists = await _db.Albums.AnyAsync(a => a.Id == id);
        if (!albumExists) return NotFound();

        var total = await _db.Songs.CountAsync(s => s.AlbumId == id);

        var songs = await _db.Songs
            .Include(s => s.Artist)
            .AsNoTracking()
            .Where(s => s.AlbumId == id)
            .OrderBy(s => s.TrackNumber)
            .ThenBy(s => s.Title)
            .Skip((page - 1) * page_size)
            .Take(page_size)
            .Select(s => new
            {
                id = s.Id,
                title = s.Title,
                artist = s.Artist != null ? s.Artist.Name : "未知艺术家",
                duration = s.Duration,
                track_number = s.TrackNumber
            })
            .ToListAsync();

        return Ok(new { songs, total });
    }
}
