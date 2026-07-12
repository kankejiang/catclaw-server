using System.IdentityModel.Tokens.Jwt;
using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.V1;

[Authorize]
[ApiController]
[Route("api/v1/ratings")]
public class RatingsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public RatingsController(ApplicationDbContext db) => _db = db;

    // POST /api/v1/ratings/{songId}  Body: { rating: 5 }
    [HttpPost("{songId}")]
    public async Task<IActionResult> SetRating(long songId, [FromBody] SetRatingRequest req)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        if (req.Rating < 1 || req.Rating > 5)
            return Ok(ApiResponse<object>.Error(ErrorCodes.InvalidParameter, "评分须在 1-5 之间"));

        if (!await _db.Songs.AnyAsync(s => s.Id == songId))
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "歌曲不存在"));

        var existing = await _db.Ratings.FirstOrDefaultAsync(r => r.UserId == userId && r.SongId == songId);
        if (existing != null)
        {
            existing.Score = req.Rating;
        }
        else
        {
            _db.Ratings.Add(new Rating { UserId = userId, SongId = songId, Score = req.Rating });
        }

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(null!));
    }

    // DELETE /api/v1/ratings/{songId}
    [HttpDelete("{songId}")]
    public async Task<IActionResult> RemoveRating(long songId)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        var rating = await _db.Ratings.FirstOrDefaultAsync(r => r.UserId == userId && r.SongId == songId);
        if (rating != null)
        {
            _db.Ratings.Remove(rating);
            await _db.SaveChangesAsync();
        }

        return Ok(ApiResponse<object>.Ok(null!));
    }

    // GET /api/v1/ratings
    [HttpGet]
    public async Task<IActionResult> GetRatings([FromQuery] int page = 1, [FromQuery] int page_size = 50)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        page = Math.Max(1, page);
        page_size = Math.Clamp(page_size, 1, 200);

        var query = _db.Ratings
            .Include(r => r.Song!)
                .ThenInclude(s => s.Artist)
            .AsNoTracking()
            .Where(r => r.UserId == userId);

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * page_size)
            .Take(page_size)
            .Select(r => new
            {
                song_id = r.SongId,
                title = r.Song!.Title,
                artist = r.Song.Artist != null ? r.Song.Artist.Name : "未知艺术家",
                rating = r.Score,
                created_at = r.CreatedAt
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

public record SetRatingRequest(int Rating);
