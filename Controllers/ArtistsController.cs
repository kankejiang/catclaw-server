using CatClawMusicServer.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ArtistsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public ArtistsController(ApplicationDbContext db)
    {
        _db = db;
    }

    // GET /api/artists?page=1&page_size=50
    [HttpGet]
    public async Task<IActionResult> GetArtists(
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 50)
    {
        if (page < 1) page = 1;
        if (page_size < 1) page_size = 50;
        if (page_size > 200) page_size = 200;

        var total = await _db.Artists.CountAsync();

        var artists = await _db.Artists
            .AsNoTracking()
            .Select(a => new
            {
                id = a.Id,
                name = a.Name,
                cover = a.Cover,
                gender = a.Gender,
                birthday = a.Birthday,
                region = a.Region,
                description = a.Description,
                song_count = _db.Songs.Count(s => s.ArtistId == a.Id)
            })
            .OrderBy(a => a.name)
            .Skip((page - 1) * page_size)
            .Take(page_size)
            .ToListAsync();

        return Ok(new { artists, total });
    }

    // GET /api/artists/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetArtist(long id)
    {
        var artist = await _db.Artists
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id);

        if (artist == null) return NotFound();

        var songCount = await _db.Songs.CountAsync(s => s.ArtistId == id);

        return Ok(new
        {
            id = artist.Id,
            name = artist.Name,
            cover = artist.Cover,
            gender = artist.Gender,
            birthday = artist.Birthday,
            region = artist.Region,
            description = artist.Description,
            song_count = songCount
        });
    }

    // GET /api/artists/{id}/songs?page=1&page_size=50
    [HttpGet("{id}/songs")]
    public async Task<IActionResult> GetArtistSongs(
        long id,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 50)
    {
        if (page < 1) page = 1;
        if (page_size < 1) page_size = 50;

        var artistExists = await _db.Artists.AnyAsync(a => a.Id == id);
        if (!artistExists) return NotFound();

        var total = await _db.Songs.CountAsync(s => s.ArtistId == id);

        var songs = await _db.Songs
            .Include(s => s.Album)
            .AsNoTracking()
            .Where(s => s.ArtistId == id)
            .OrderBy(s => s.TrackNumber)
            .ThenBy(s => s.Title)
            .Skip((page - 1) * page_size)
            .Take(page_size)
            .Select(s => new
            {
                id = s.Id,
                title = s.Title,
                album = s.Album != null ? s.Album.Title : "未知专辑",
                duration = s.Duration,
                track_number = s.TrackNumber
            })
            .ToListAsync();

        return Ok(new { songs, total });
    }
}
