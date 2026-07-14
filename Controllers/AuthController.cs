using CatClawMusicServer.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AdminCredentialStore _store;
    private readonly IDbContextFactory<ApplicationDbContext> _dbf;

    public AuthController(AdminCredentialStore store, IDbContextFactory<ApplicationDbContext> dbf)
    {
        _store = store;
        _dbf = dbf;
    }

    // 检查是否已配置（旧系统 AdminCredentialStore 或 数据库 Users 任一存在即视为已配置）
    private async Task<bool> IsAlreadyConfiguredAsync()
    {
        if (_store.IsConfigured) return true;
        await using var db = await _dbf.CreateDbContextAsync();
        return await db.Users.AnyAsync();
    }

    // POST /api/auth/register — 仅首次（未配置管理员时）可用
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        if (await IsAlreadyConfiguredAsync())
            return BadRequest(new { message = "管理员已注册，请直接登录" });

        if (string.IsNullOrWhiteSpace(req.Username) || req.Username.Length < 2)
            return BadRequest(new { message = "用户名至少 2 个字符" });

        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 4)
            return BadRequest(new { message = "密码至少 4 个字符" });

        var ok = await _store.TrySetCredentialsAsync(req.Username, req.Password);
        if (!ok)
            return BadRequest(new { message = "注册失败，管理员已存在" });

        // 注册成功 → 自动登录
        return SetSessionAndOk(req.Username);
    }

    // POST /api/auth/login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (!await IsAlreadyConfiguredAsync())
            return Unauthorized(new { message = "尚未注册管理员，请先注册" });

        // 优先用旧系统凭据校验
        if (_store.IsConfigured && req.Username == _store.AdminUser && req.Password == _store.AdminPassword)
            return SetSessionAndOk(_store.AdminUser);

        // 回退到数据库 Users 校验（BCrypt）
        await using var db = await _dbf.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == req.Username);
        if (user != null && BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return SetSessionAndOk(user.Username);

        return Unauthorized(new { message = "用户名或密码错误" });
    }

    // POST /api/auth/logout
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("catclaw_session", new CookieOptions
        {
            Path = "/",
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
        });
        return Ok(new { message = "已登出" });
    }

    // GET /api/auth/status
    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var configured = await IsAlreadyConfiguredAsync();
        var session = Request.Cookies["catclaw_session"];
        var loggedIn = configured
            ? session == _store.AccessToken
            : true; // 未配置管理员时全部放行

        return Ok(new
        {
            loggedIn,
            user = _store.AdminUser,
            configured
        });
    }

    private IActionResult SetSessionAndOk(string user)
    {
        Response.Cookies.Append("catclaw_session", _store.AccessToken, new CookieOptions
        {
            Path = "/",
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(30),
        });
        return Ok(new { message = "成功", user });
    }
}

public record LoginRequest(string Username, string Password);
public record RegisterRequest(string Username, string Password);
