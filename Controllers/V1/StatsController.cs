using System.IdentityModel.Tokens.Jwt;
using CatClawMusicServer.Models;
using CatClawMusicServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CatClawMusicServer.Controllers.V1;

[Authorize]
[ApiController]
[Route("api/v1/stats")]
public class StatsController : ControllerBase
{
    private readonly StatsService _statsService;

    public StatsController(StatsService statsService) => _statsService = statsService;

    // GET /api/v1/stats?days=30
    [HttpGet]
    public async Task<IActionResult> GetUserStats([FromQuery] int days = 30)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        var stats = await _statsService.GetUserStatsAsync(userId, days);
        return Ok(ApiResponse<object>.Ok(stats));
    }

    // GET /api/v1/stats/overview (admin only)
    [HttpGet("overview")]
    public async Task<IActionResult> GetServerStats()
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (role != "admin")
            return Ok(ApiResponse<object>.Error(ErrorCodes.Forbidden, "仅管理员可访问"));

        var stats = await _statsService.GetServerStatsAsync();
        return Ok(ApiResponse<object>.Ok(stats));
    }

    private long GetUserId()
    {
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return long.TryParse(sub, out var id) ? id : 0;
    }
}
