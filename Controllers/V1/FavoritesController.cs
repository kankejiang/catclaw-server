using System.IdentityModel.Tokens.Jwt;
using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.V1;

[Authorize]
[ApiController]
[Route("api/v1/favorites")]
public class FavoritesController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public FavoritesController(ApplicationDbContext db) => _db = db;

    // GET /api/v1/favorites?page=1&page_size=50
    [HttpGet]
    public async Task<IActionResult> GetFavorites([FromQuery] int page = 1, [FromQuery] int page_size = 50)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        page = Math.Max(1, page);
        page_size = Math.Clamp(page_size, 1, 200);

        var query = _db.Favorites
            .Include(f => f.Song!)
                .ThenInclude(s => s.Artist)
            .Include(f => f.Song!)
                .ThenInclude(s => s.Album)
            .AsNoTracking()
            .Where(f => f.UserId == userId);

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip((page - 1) * page_size)
            .Take(page_size)
            .Select(f => new
            {
                id = f.Song!.Id,
                title = f.Song.Title,
                artist = f.Song.Artist != null ? f.Song.Artist.Name : "未知艺术家",
                album = f.Song.Album != null ? f.Song.Album.Title : "未知专辑",
                album_cover = f.Song.Album != null ? f.Song.Album.Cover : f.Song.CoverArtPath,
                duration = f.Song.Duration,
                favorited_at = f.CreatedAt
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            items,
            total,
            page,
            page_size
        }));
    }

    // POST /api/v1/favorites/{songId}
    [HttpPost("{songId}")]
    public async Task<IActionResult> AddFavorite(long songId)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        if (!await _db.Songs.AnyAsync(s => s.Id == songId))
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "歌曲不存在"));

        if (await _db.Favorites.AnyAsync(f => f.UserId == userId && f.SongId == songId))
            return Ok(ApiResponse<object>.Ok(null!)); // 幂等

        _db.Favorites.Add(new Favorite { UserId = userId, SongId = songId });
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(null!));
    }

    // DELETE /api/v1/favorites/{songId}
    [HttpDelete("{songId}")]
    public async Task<IActionResult> RemoveFavorite(long songId)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        var fav = await _db.Favorites.FirstOrDefaultAsync(f => f.UserId == userId && f.SongId == songId);
        if (fav != null)
        {
            _db.Favorites.Remove(fav);
            await _db.SaveChangesAsync();
        }

        return Ok(ApiResponse<object>.Ok(null!));
    }

    // GET /api/v1/favorites/check?song_ids=1,2,3
    [HttpGet("check")]
    public async Task<IActionResult> CheckFavorites([FromQuery] string song_ids)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        var ids = song_ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => long.TryParse(s, out var v) ? v : 0)
            .Where(v => v > 0)
            .Distinct()
            .ToList();

        var favorited = await _db.Favorites
            .Where(f => f.UserId == userId && ids.Contains(f.SongId))
            .Select(f => f.SongId)
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { favorited_ids = favorited }));
    }

    private long GetUserId()
    {
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return long.TryParse(sub, out var id) ? id : 0;
    }
}
