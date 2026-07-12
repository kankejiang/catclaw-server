using Microsoft.AspNetCore.Mvc;

namespace CatClawMusicServer.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AdminCredentialStore _store;

    public AuthController(AdminCredentialStore store) => _store = store;

    // POST /api/auth/register — 仅首次（未配置管理员时）可用
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        if (_store.IsConfigured)
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
    public IActionResult Login([FromBody] LoginRequest req)
    {
        if (!_store.IsConfigured)
            return Unauthorized(new { message = "尚未注册管理员，请先注册" });

        if (req.Username == _store.AdminUser && req.Password == _store.AdminPassword)
            return SetSessionAndOk(_store.AdminUser);

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
    public IActionResult Status()
    {
        var session = Request.Cookies["catclaw_session"];
        var loggedIn = _store.IsConfigured
            ? session == _store.AccessToken
            : true; // 未配置管理员时全部放行

        return Ok(new
        {
            loggedIn,
            user = _store.AdminUser,
            configured = _store.IsConfigured
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
