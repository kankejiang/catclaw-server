using CatClawMusicServer.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public SearchController(ApplicationDbContext db)
    {
        _db = db;
    }

    // GET /api/search?q=keyword&page=1&page_size=50
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string q = "",
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 50)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(new { results = new object[0], total = 0 });

        if (page < 1) page = 1;
        if (page_size < 1) page_size = 50;
        if (page_size > 200) page_size = 200;

        var keyword = $"%{q}%";

        var total = await _db.Songs
            .Include(s => s.Artist)
            .Include(s => s.Album)
            .AsNoTracking()
            .CountAsync(s =>
                EF.Functions.Like(s.Title, keyword) ||
                EF.Functions.Like(s.Artist != null ? s.Artist.Name : "", keyword) ||
                EF.Functions.Like(s.Album != null ? s.Album.Title : "", keyword));

        var songs = await _db.Songs
            .Include(s => s.Artist)
            .Include(s => s.Album)
            .AsNoTracking()
            .Where(s =>
                EF.Functions.Like(s.Title, keyword) ||
                EF.Functions.Like(s.Artist != null ? s.Artist.Name : "", keyword) ||
                EF.Functions.Like(s.Album != null ? s.Album.Title : "", keyword))
            .OrderBy(s => s.Title)
            .Skip((page - 1) * page_size)
            .Take(page_size)
            .Select(s => new
            {
                id = s.Id,
                title = s.Title,
                artist = s.Artist != null ? s.Artist.Name : "未知艺术家",
                album = s.Album != null ? s.Album.Title : "未知专辑",
                duration = s.Duration
            })
            .ToListAsync();

        return Ok(new { results = songs, total });
    }
}
