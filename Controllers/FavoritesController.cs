using CatClawMusicServer.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FavoritesController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public FavoritesController(ApplicationDbContext db)
    {
        _db = db;
    }

    // GET /api/favorites?page=1&page_size=50
    [HttpGet]
    public async Task<IActionResult> GetFavorites(
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 50)
    {
        if (page < 1) page = 1;
        if (page_size < 1) page_size = 50;

        var total = await _db.Favorites.CountAsync();
        var songs = await _db.Favorites
            .Include(f => f.Song!.Artist)
            .Include(f => f.Song.Album)
            .AsNoTracking()
            .OrderByDescending(f => f.CreatedAt)
            .Skip((page - 1) * page_size)
            .Take(page_size)
            .Select(f => new
            {
                id = f.Song!.Id,
                title = f.Song.Title,
                artist = f.Song.Artist != null ? f.Song.Artist.Name : "未知艺术家",
                album = f.Song.Album != null ? f.Song.Album.Title : "未知专辑",
                duration = f.Song.Duration,
                created_at = f.CreatedAt
            })
            .ToListAsync();

        return Ok(new { songs, total });
    }

    // POST /api/favorites  body: { "songId": 123 }
    [HttpPost]
    public async Task<IActionResult> AddFavorite([FromBody] AddFavoriteRequest req)
    {
        var exists = await _db.Favorites.AnyAsync(f => f.SongId == req.SongId);
        if (exists) return Ok(new { message = "already favorited" });

        var songExists = await _db.Songs.AnyAsync(s => s.Id == req.SongId);
        if (!songExists) return BadRequest("song not found");

        _db.Favorites.Add(new Models.Favorite { SongId = req.SongId });
        await _db.SaveChangesAsync();
        return Ok(new { message = "favorited" });
    }

    // DELETE /api/favorites/{songId}
    [HttpDelete("{songId}")]
    public async Task<IActionResult> RemoveFavorite(long songId)
    {
        var entry = await _db.Favorites.FirstOrDefaultAsync(f => f.SongId == songId);
        if (entry == null) return NotFound();
        _db.Favorites.Remove(entry);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record AddFavoriteRequest(long SongId);
