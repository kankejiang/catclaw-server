using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using CatClawMusicServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.V1;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly JwtService _jwt;
    private readonly AdminCredentialStore _adminStore;
    private readonly ILogger<AuthController> _logger;

    public AuthController(ApplicationDbContext db, JwtService jwt,
        AdminCredentialStore adminStore, ILogger<AuthController> logger)
    {
        _db = db;
        _jwt = jwt;
        _adminStore = adminStore;
        _logger = logger;
    }

    // ── 注册（首次启动创建管理员，或管理员创建用户）──
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return Ok(ApiResponse<object>.Error(ErrorCodes.InvalidParameter, "用户名和密码不能为空"));

        if (req.Password.Length < 6)
            return Ok(ApiResponse<object>.Error(ErrorCodes.InvalidParameter, "密码至少6位"));

        // 检查是否已有用户
        var userCount = await _db.Users.CountAsync();
        string role;

        if (userCount == 0)
        {
            // 首个用户自动成为管理员
            role = "admin";
        }
        else
        {
            // 检查请求者是否为管理员
            var authHeader = Request.Headers.Authorization.ToString();
            if (!authHeader.StartsWith("Bearer ") || !IsAdmin(authHeader["Bearer ".Length..]))
                return Ok(ApiResponse<object>.Error(ErrorCodes.Forbidden, "仅管理员可创建用户"));
            role = "user";
        }

        // 检查用户名是否已存在
        if (await _db.Users.AnyAsync(u => u.Username == req.Username))
            return Ok(ApiResponse<object>.Error(ErrorCodes.DuplicateEntry, "用户名已存在"));

        var user = new User
        {
            Username = req.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            DisplayName = req.DisplayName ?? req.Username,
            Role = role
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _logger.LogInformation("用户注册: {Username} (role={Role})", user.Username, user.Role);

        return Ok(ApiResponse<object>.Ok(new
        {
            user_id = user.Id,
            username = user.Username,
            display_name = user.DisplayName,
            role = user.Role
        }));
    }

    // ── 登录 ──
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return Ok(ApiResponse<object>.Error(ErrorCodes.InvalidParameter, "用户名和密码不能为空"));

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == req.Username);
        if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Ok(ApiResponse<object>.Error(ErrorCodes.InvalidCredentials, "用户名或密码错误"));

        // 生成 Token
        var accessToken = _jwt.GenerateAccessToken(user.Id, user.Username, user.Role);
        var refreshToken = _jwt.GenerateRefreshToken();

        // 保存 RefreshToken
        var rt = new RefreshToken
        {
            UserId = user.Id,
            Token = refreshToken,
            DeviceName = req.DeviceName ?? "Unknown",
            DeviceId = req.DeviceId,
            ExpiresAt = DateTime.UtcNow.AddDays(30) // TODO: use JwtOptions
        };
        _db.RefreshTokens.Add(rt);

        // 更新最后登录时间
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // 注册/更新设备
        if (!string.IsNullOrEmpty(req.DeviceId))
        {
            var device = await _db.Devices
                .FirstOrDefaultAsync(d => d.UserId == user.Id && d.DeviceId == req.DeviceId);

            if (device == null)
            {
                device = new Device
                {
                    UserId = user.Id,
                    DeviceId = req.DeviceId,
                    DeviceName = req.DeviceName ?? "Unknown",
                    Platform = req.Platform ?? "web"
                };
                _db.Devices.Add(device);
            }
            else
            {
                device.LastSeenAt = DateTime.UtcNow;
                device.DeviceName = req.DeviceName ?? device.DeviceName;
            }
            await _db.SaveChangesAsync();
        }

        _logger.LogInformation("用户登录: {Username} device={Device}", user.Username, req.DeviceName);

        return Ok(ApiResponse<object>.Ok(new
        {
            access_token = accessToken,
            refresh_token = refreshToken,
            expires_in = 15 * 60, // seconds
            token_type = "Bearer",
            user = new
            {
                id = user.Id,
                username = user.Username,
                display_name = user.DisplayName,
                role = user.Role,
                avatar = user.AvatarPath
            }
        }));
    }

    // ── 刷新 Token ──
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken))
            return Ok(ApiResponse<object>.Error(ErrorCodes.InvalidParameter, "refresh_token 不能为空"));

        var storedToken = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == req.RefreshToken && !rt.IsRevoked);

        if (storedToken == null || storedToken.ExpiresAt < DateTime.UtcNow)
            return Ok(ApiResponse<object>.Error(ErrorCodes.TokenExpired, "refresh_token 无效或已过期"));

        // 吊销旧 RefreshToken
        storedToken.IsRevoked = true;

        // 生成新 Token 对
        var user = storedToken.User!;
        var newAccessToken = _jwt.GenerateAccessToken(user.Id, user.Username, user.Role);
        var newRefreshToken = _jwt.GenerateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = newRefreshToken,
            DeviceName = storedToken.DeviceName,
            DeviceId = storedToken.DeviceId,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });

        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            access_token = newAccessToken,
            refresh_token = newRefreshToken,
            expires_in = 15 * 60,
            token_type = "Bearer"
        }));
    }

    // ── 注销 ──
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest req)
    {
        if (!string.IsNullOrWhiteSpace(req.RefreshToken))
        {
            var token = await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == req.RefreshToken);
            if (token != null)
            {
                token.IsRevoked = true;
                await _db.SaveChangesAsync();
            }
        }
        return Ok(ApiResponse<object>.Ok(null!));
    }

    // ── 当前用户信息 ──
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = GetCurrentUserId();
        if (userId == 0)
            return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        var user = await _db.Users.FindAsync(userId);
        if (user == null)
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "用户不存在"));

        return Ok(ApiResponse<object>.Ok(new
        {
            id = user.Id,
            username = user.Username,
            display_name = user.DisplayName,
            role = user.Role,
            avatar = user.AvatarPath,
            created_at = user.CreatedAt,
            last_login_at = user.LastLoginAt
        }));
    }

    // ── 修改密码 ──
    [Authorize]
    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var userId = GetCurrentUserId();
        if (userId == 0)
            return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        var user = await _db.Users.FindAsync(userId);
        if (user == null)
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "用户不存在"));

        if (!BCrypt.Net.BCrypt.Verify(req.OldPassword, user.PasswordHash))
            return Ok(ApiResponse<object>.Error(ErrorCodes.InvalidCredentials, "原密码错误"));

        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 6)
            return Ok(ApiResponse<object>.Error(ErrorCodes.InvalidParameter, "新密码至少6位"));

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        await _db.SaveChangesAsync();

        // 吊销所有 RefreshToken（强制重新登录）
        var tokens = await _db.RefreshTokens.Where(rt => rt.UserId == userId && !rt.IsRevoked).ToListAsync();
        foreach (var t in tokens) t.IsRevoked = true;
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(null!));
    }

    // ── 辅助方法 ──
    private long GetCurrentUserId()
    {
        var sub = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        return long.TryParse(sub, out var id) ? id : 0;
    }

    private bool IsAdmin(string token)
    {
        var principal = _jwt.ValidateToken(token);
        return principal != null && JwtService.GetRole(principal) == "admin";
    }
}

// ── 请求 DTO ──
public record RegisterRequest(string Username, string Password, string? DisplayName);
public record LoginRequest(string Username, string Password, string? DeviceName, string? DeviceId, string? Platform);
public record RefreshRequest(string RefreshToken);
public record LogoutRequest(string? RefreshToken);
public record ChangePasswordRequest(string OldPassword, string NewPassword);
