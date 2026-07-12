using System.IdentityModel.Tokens.Jwt;
using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.V1;

[Authorize]
[ApiController]
[Route("api/v1/scrobble")]
public class ScrobbleController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public ScrobbleController(ApplicationDbContext db) => _db = db;

    // POST /api/v1/scrobble
    [HttpPost]
    public async Task<IActionResult> Scrobble([FromBody] ScrobbleRequest req)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        if (req.SongId <= 0)
            return Ok(ApiResponse<object>.Error(ErrorCodes.InvalidParameter, "song_id 无效"));

        if (!await _db.Songs.AnyAsync(s => s.Id == req.SongId))
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "歌曲不存在"));

        _db.Scrobbles.Add(new Scrobble
        {
            UserId = userId,
            SongId = req.SongId,
            Timestamp = req.Timestamp ?? DateTime.UtcNow,
            DurationPlayedMs = req.DurationPlayedMs,
            Source = req.Source ?? "library"
        });

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(null!));
    }

    // GET /api/v1/history?page=1&page_size=50
    [HttpGet("/api/v1/history")]
    public async Task<IActionResult> GetHistory([FromQuery] int page = 1, [FromQuery] int page_size = 50)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        page = Math.Max(1, page);
        page_size = Math.Clamp(page_size, 1, 200);

        var query = _db.Scrobbles
            .Include(s => s.Song!)
                .ThenInclude(s => s.Artist)
            .AsNoTracking()
            .Where(s => s.UserId == userId);

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(s => s.Timestamp)
            .Skip((page - 1) * page_size)
            .Take(page_size)
            .Select(s => new
            {
                song_id = s.SongId,
                title = s.Song!.Title,
                artist = s.Song.Artist != null ? s.Song.Artist.Name : "未知艺术家",
                timestamp = s.Timestamp,
                duration_played_ms = s.DurationPlayedMs,
                source = s.Source
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { items, total, page, page_size }));
    }

    private long GetUserId()
    {
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return long.TryParse(sub, out var id) ? id : 0;
    }
}

public record ScrobbleRequest(long SongId, DateTime? Timestamp, int DurationPlayedMs, string? Source);
