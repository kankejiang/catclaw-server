using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.V1;

[Authorize]
[ApiController]
[Route("api/v1/artists")]
public class ArtistsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public ArtistsController(ApplicationDbContext db) => _db = db;

    // GET /api/v1/artists?page=1&page_size=50&q=
    [HttpGet]
    public async Task<IActionResult> GetArtists(
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 50,
        [FromQuery] string? q = null)
    {
        page = Math.Max(1, page);
        page_size = Math.Clamp(page_size, 1, 200);

        var query = _db.Artists.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(a => a.Name.Contains(q));

        var total = await query.CountAsync();

        var artists = await query
            .OrderBy(a => a.Name)
            .Skip((page - 1) * page_size)
            .Take(page_size)
            .Select(a => new
            {
                id = a.Id,
                name = a.Name,
                cover = a.Cover,
                song_count = a.Songs != null ? a.Songs.Count : 0,
                album_count = a.Albums != null ? a.Albums.Count : 0
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            items = artists,
            total,
            page,
            page_size,
            total_pages = (int)Math.Ceiling((double)total / page_size)
        }));
    }

    // GET /api/v1/artists/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetArtist(long id)
    {
        var artist = await _db.Artists
            .Include(a => a.Songs)
            .Include(a => a.Albums)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id);

        if (artist == null)
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "艺术家不存在"));

        return Ok(ApiResponse<object>.Ok(new
        {
            id = artist.Id,
            name = artist.Name,
            cover = artist.Cover,
            gender = artist.Gender,
            birthday = artist.Birthday,
            region = artist.Region,
            description = artist.Description,
            song_count = artist.Songs?.Count ?? 0,
            album_count = artist.Albums?.Count ?? 0
        }));
    }

    // GET /api/v1/artists/{id}/songs
    [HttpGet("{id}/songs")]
    public async Task<IActionResult> GetArtistSongs(long id, [FromQuery] int page = 1, [FromQuery] int page_size = 50)
    {
        page = Math.Max(1, page);
        page_size = Math.Clamp(page_size, 1, 200);

        var query = _db.Songs
            .Include(s => s.Album)
            .AsNoTracking()
            .Where(s => s.ArtistId == id);

        var total = await query.CountAsync();
        var songs = await query
            .OrderBy(s => s.Title)
            .Skip((page - 1) * page_size)
            .Take(page_size)
            .Select(s => new
            {
                id = s.Id,
                title = s.Title,
                album = s.Album != null ? s.Album.Title : "未知专辑",
                album_cover = s.Album != null ? s.Album.Cover : s.CoverArtPath,
                duration = s.Duration,
                year = s.Year
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            items = songs,
            total,
            page,
            page_size
        }));
    }

    // GET /api/v1/artists/{id}/albums
    [HttpGet("{id}/albums")]
    public async Task<IActionResult> GetArtistAlbums(long id)
    {
        var albums = await _db.Albums
            .Where(a => a.ArtistId == id)
            .AsNoTracking()
            .Select(a => new
            {
                id = a.Id,
                title = a.Title,
                cover = a.Cover,
                song_count = a.Songs != null ? a.Songs.Count : 0
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { items = albums }));
    }
}
