using System.IdentityModel.Tokens.Jwt;
using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using CatClawMusicServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.V1;

[Authorize]
[ApiController]
[Route("api/v1/recommend")]
public class RecommendController : ControllerBase
{
    private readonly RecommendService _recommend;
    private readonly ApplicationDbContext _db;

    public RecommendController(RecommendService recommend, ApplicationDbContext db)
    {
        _recommend = recommend;
        _db = db;
    }

    // GET /api/v1/recommend/daily
    [HttpGet("daily")]
    public async Task<IActionResult> DailyRecommend([FromQuery] int count = 30)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        var songIds = await _recommend.GenerateDailyRecommendAsync(userId, Math.Clamp(count, 1, 100));
        var songs = await GetSongDetails(songIds);
        return Ok(ApiResponse<object>.Ok(songs));
    }

    // GET /api/v1/recommend/recent
    [HttpGet("recent")]
    public async Task<IActionResult> RecentlyPlayed([FromQuery] int count = 50)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        var songIds = await _recommend.GetRecentlyPlayedAsync(userId, Math.Clamp(count, 1, 100));
        var songs = await GetSongDetails(songIds);
        return Ok(ApiResponse<object>.Ok(songs));
    }

    // GET /api/v1/recommend/top
    [HttpGet("top")]
    public async Task<IActionResult> TopPlayed([FromQuery] int count = 100, [FromQuery] int days = 30)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        var songIds = await _recommend.GetTopPlayedAsync(userId, Math.Clamp(count, 1, 200), days);
        var songs = await GetSongDetails(songIds);
        return Ok(ApiResponse<object>.Ok(songs));
    }

    // GET /api/v1/recommend/discover
    [HttpGet("discover")]
    public async Task<IActionResult> Discover([FromQuery] int count = 30)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        var songIds = await _recommend.GetDiscoverAsync(userId, Math.Clamp(count, 1, 100));
        var songs = await GetSongDetails(songIds);
        return Ok(ApiResponse<object>.Ok(songs));
    }

    // GET /api/v1/recommend/artist-mix?artist_id=123
    [HttpGet("artist-mix")]
    public async Task<IActionResult> ArtistMix([FromQuery] long artist_id, [FromQuery] int count = 30)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        var songIds = await _recommend.GetArtistMixAsync(artist_id, userId, Math.Clamp(count, 1, 100));
        var songs = await GetSongDetails(songIds);
        return Ok(ApiResponse<object>.Ok(songs));
    }

    private async Task<List<object>> GetSongDetails(List<long> songIds)
    {
        if (songIds.Count == 0) return new List<object>();

        var songs = await _db.Songs
            .Include(s => s.Artist)
            .Include(s => s.Album)
            .Where(s => songIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id);

        // 保持推荐顺序
        return songIds
            .Where(id => songs.ContainsKey(id))
            .Select(id =>
            {
                var s = songs[id];
                return (object)new
                {
                    id = s.Id,
                    title = s.Title,
                    artist = s.Artist?.Name ?? "未知艺术家",
                    album = s.Album?.Title ?? "未知专辑",
                    album_cover = s.Album?.Cover ?? s.CoverArtPath,
                    duration = s.Duration
                };
            })
            .ToList();
    }

    private long GetUserId()
    {
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return long.TryParse(sub, out var id) ? id : 0;
    }
}
