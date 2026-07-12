using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.V1;

[Authorize]
[ApiController]
[Route("api/v1/genres")]
public class GenreController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public GenreController(ApplicationDbContext db) => _db = db;

    // GET /api/v1/genres
    [HttpGet]
    public async Task<IActionResult> GetGenres()
    {
        var genreData = await _db.Songs
            .AsNoTracking()
            .Where(s => !string.IsNullOrEmpty(s.Genre))
            .GroupBy(s => s.Genre!)
            .Select(g => new
            {
                Name = g.Key,
                SongCount = g.Count(),
                AlbumCount = g.Select(s => s.AlbumId).Distinct().Count()
            })
            .OrderByDescending(g => g.SongCount)
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { items = genreData }));
    }

    // GET /api/v1/genres/{name}/songs?page=1&page_size=50
    [HttpGet("{name}/songs")]
    public async Task<IActionResult> GetGenreSongs(string name,
        [FromQuery] int page = 1, [FromQuery] int page_size = 50)
    {
        page = Math.Max(1, page);
        page_size = Math.Clamp(page_size, 1, 200);

        var query = _db.Songs
            .Include(s => s.Artist)
            .Include(s => s.Album)
            .AsNoTracking()
            .Where(s => s.Genre == name);

        var total = await query.CountAsync();

        var songs = await query
            .OrderBy(s => s.Title)
            .Skip((page - 1) * page_size)
            .Take(page_size)
            .Select(s => new
            {
                id = s.Id,
                title = s.Title,
                artist = s.Artist != null ? s.Artist.Name : "未知艺术家",
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
}
