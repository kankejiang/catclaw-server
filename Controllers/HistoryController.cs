using CatClawMusicServer.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HistoryController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public HistoryController(ApplicationDbContext db)
    {
        _db = db;
    }

    // GET /api/history?page=1&page_size=50
    [HttpGet]
    public async Task<IActionResult> GetHistory(
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 50)
    {
        if (page < 1) page = 1;
        if (page_size < 1) page_size = 50;

        var total = await _db.PlayHistories.CountAsync();

        var songs = await _db.PlayHistories
            .Include(h => h.Song)
            .ThenInclude(s => s!.Artist)
            .Include(h => h.Song)
            .ThenInclude(s => s!.Album)
            .AsNoTracking()
            .OrderByDescending(h => h.PlayedAt)
            .Skip((page - 1) * page_size)
            .Take(page_size)
            .Select(h => new
            {
                id = h.Song!.Id,
                title = h.Song.Title,
                artist = h.Song.Artist != null ? h.Song.Artist.Name : "未知艺术家",
                album = h.Song.Album != null ? h.Song.Album.Title : "未知专辑",
                duration = h.Song.Duration,
                played_at = h.PlayedAt,
                play_duration = h.PlayDuration
            })
            .ToListAsync();

        return Ok(new { songs, total });
    }

    // POST /api/history
    [HttpPost]
    public async Task<IActionResult> AddHistory([FromBody] AddHistoryRequest req)
    {
        var songExists = await _db.Songs.AnyAsync(s => s.Id == req.SongId);
        if (!songExists) return BadRequest("song not found");

        _db.PlayHistories.Add(new Models.PlayHistory
        {
            SongId = req.SongId,
            PlayDuration = req.PlayDuration
        });

        await _db.SaveChangesAsync();
        return Ok(new { message = "recorded" });
    }

    // DELETE /api/history
    [HttpDelete]
    public async Task<IActionResult> ClearHistory()
    {
        _db.PlayHistories.RemoveRange(_db.PlayHistories);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record AddHistoryRequest(long SongId, int PlayDuration);
