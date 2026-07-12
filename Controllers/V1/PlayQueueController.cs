using System.IdentityModel.Tokens.Jwt;
using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.V1;

[Authorize]
[ApiController]
[Route("api/v1/playqueue")]
public class PlayQueueController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public PlayQueueController(ApplicationDbContext db) => _db = db;

    // GET /api/v1/playqueue
    [HttpGet]
    public async Task<IActionResult> GetPlayQueue()
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        var pq = await _db.PlayQueues.FirstOrDefaultAsync(p => p.UserId == userId);
        if (pq == null)
            return Ok(ApiResponse<object>.Ok(new { song_ids = Array.Empty<long>(), current_index = 0, position_ms = 0 }));

        return Ok(ApiResponse<object>.Ok(new
        {
            song_ids = System.Text.Json.JsonSerializer.Deserialize<long[]>(pq.SongIds) ?? Array.Empty<long>(),
            current_index = pq.CurrentIndex,
            position_ms = pq.PositionMs,
            updated_at = pq.UpdatedAt
        }));
    }

    // PUT /api/v1/playqueue
    [HttpPut]
    public async Task<IActionResult> SavePlayQueue([FromBody] PlayQueueRequest req)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        if (req.SongIds == null || req.SongIds.Length > 500)
            return Ok(ApiResponse<object>.Error(ErrorCodes.InvalidParameter, "队列歌曲数量须在 1-500 之间"));

        var pq = await _db.PlayQueues.FirstOrDefaultAsync(p => p.UserId == userId);
        if (pq == null)
        {
            pq = new PlayQueue { UserId = userId };
            _db.PlayQueues.Add(pq);
        }

        pq.SongIds = System.Text.Json.JsonSerializer.Serialize(req.SongIds);
        pq.CurrentIndex = Math.Max(0, req.CurrentIndex);
        pq.PositionMs = Math.Max(0, req.PositionMs);
        pq.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(null!));
    }

    // DELETE /api/v1/playqueue
    [HttpDelete]
    public async Task<IActionResult> ClearPlayQueue()
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        var pq = await _db.PlayQueues.FirstOrDefaultAsync(p => p.UserId == userId);
        if (pq != null)
        {
            _db.PlayQueues.Remove(pq);
            await _db.SaveChangesAsync();
        }

        return Ok(ApiResponse<object>.Ok(null!));
    }

    private long GetUserId()
    {
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return long.TryParse(sub, out var id) ? id : 0;
    }
}

public record PlayQueueRequest(long[] SongIds, int CurrentIndex, long PositionMs);
