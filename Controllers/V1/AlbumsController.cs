using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.V1;

[Authorize]
[ApiController]
[Route("api/v1/albums")]
public class AlbumsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public AlbumsController(ApplicationDbContext db) => _db = db;

    // GET /api/v1/albums?page=1&page_size=50&artist_id=&sort=
    [HttpGet]
    public async Task<IActionResult> GetAlbums(
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 50,
        [FromQuery] long? artist_id = null,
        [FromQuery] string? sort = null)
    {
        page = Math.Max(1, page);
        page_size = Math.Clamp(page_size, 1, 200);

        var query = _db.Albums
            .Include(a => a.Artist)
            .AsNoTracking()
            .AsQueryable();

        if (artist_id.HasValue)
            query = query.Where(a => a.ArtistId == artist_id.Value);

        query = sort?.ToLower() switch
        {
            "year" => query.OrderBy(a => a.Title),  // TODO: Album 目前没有 Year 字段
            "name" => query.OrderBy(a => a.Title),
            _ => query.OrderBy(a => a.Title)
        };

        var total = await query.CountAsync();

        var albums = await query
            .Skip((page - 1) * page_size)
            .Take(page_size)
            .Select(a => new
            {
                id = a.Id,
                title = a.Title,
                artist = a.Artist != null ? a.Artist.Name : "未知艺术家",
                artist_id = a.ArtistId,
                cover = a.Cover,
                song_count = a.Songs != null ? a.Songs.Count : 0
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            items = albums,
            total,
            page,
            page_size,
            total_pages = (int)Math.Ceiling((double)total / page_size)
        }));
    }

    // GET /api/v1/albums/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetAlbum(long id)
    {
        var album = await _db.Albums
            .Include(a => a.Artist)
            .Include(a => a.Songs!)
                .ThenInclude(s => s.Artist)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id);

        if (album == null)
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "专辑不存在"));

        return Ok(ApiResponse<object>.Ok(new
        {
            id = album.Id,
            title = album.Title,
            artist = album.Artist?.Name ?? "未知艺术家",
            artist_id = album.ArtistId,
            cover = album.Cover,
            songs = album.Songs?
                .OrderBy(s => s.DiscNumber)
                .ThenBy(s => s.TrackNumber)
                .Select(s => new
                {
                    id = s.Id,
                    title = s.Title,
                    artist = s.Artist?.Name ?? "未知艺术家",
                    duration = s.Duration,
                    track_number = s.TrackNumber,
                    disc_number = s.DiscNumber
                })
                .ToList()
        }));
    }

    // GET /api/v1/albums/{id}/songs
    [HttpGet("{id}/songs")]
    public async Task<IActionResult> GetAlbumSongs(long id)
    {
        var songs = await _db.Songs
            .Include(s => s.Artist)
            .AsNoTracking()
            .Where(s => s.AlbumId == id)
            .OrderBy(s => s.DiscNumber)
            .ThenBy(s => s.TrackNumber)
            .Select(s => new
            {
                id = s.Id,
                title = s.Title,
                artist = s.Artist != null ? s.Artist.Name : "未知艺术家",
                duration = s.Duration,
                track_number = s.TrackNumber,
                disc_number = s.DiscNumber
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { items = songs }));
    }
}
