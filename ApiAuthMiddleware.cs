using System.Security.Cryptography;
using System.Text;

using CatClawMusicServer.Services;

namespace CatClawMusicServer;

/// <summary>
/// 内网鉴权中间件：保护 /api 与 /rest 端点。
/// <list type="bullet">
///   <item>/rest/*（Subsonic 兼容）：使用标准 Subsonic token 认证（t = md5(password + salt)）。</item>
///   <item>/api/*：使用 Bearer token。</item>
/// </list>
/// 若未配置 AccessToken，则全部放行（本地开发友好）。
/// </summary>
public class ApiAuthMiddleware
{
    private readonly RequestDelegate _next;

    public ApiAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ServerAuthOptions auth, JwtService jwtService)
    {
        var path = context.Request.Path.Value ?? "";

        // /api/auth/* 和 /api/v1/auth/* 为公共端点（注册/登录/登出/状态），不校验 AccessToken
        // /api/config + /api/scan/status 为设置页展示用，无需鉴权
        if (path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/v1/auth", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/config", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/scan/status", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // /api/v1/* 由 ASP.NET Core JWT Bearer 中间件处理认证，此处放行
        if (path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var isRest = path.StartsWith("/rest/", StringComparison.OrdinalIgnoreCase);
        var isApi = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);

        if ((isRest || isApi) && !string.IsNullOrEmpty(auth.AccessToken))
        {
            bool ok = false;

            if (isRest)
            {
                // Subsonic token 认证: t == md5(password + salt)
                var t = context.Request.Query["t"].ToString();
                var s = context.Request.Query["s"].ToString();
                if (!string.IsNullOrEmpty(t) && !string.IsNullOrEmpty(s))
                {
                    var hash = MD5.HashData(Encoding.UTF8.GetBytes(auth.AccessToken + s));
                    var expected = Convert.ToHexString(hash).ToLowerInvariant();
                    ok = expected == t;
                }
            }
            else // /api
            {
                var authHeader = context.Request.Headers["Authorization"].ToString();
                if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    var token = authHeader["Bearer ".Length..].Trim();
                    // 先检查静态 AccessToken
                    ok = token == auth.AccessToken;
                    // 再检查 JWT
                    if (!ok)
                    {
                        var principal = jwtService.ValidateToken(token);
                        ok = principal != null;
                    }
                }

                // Web UI 登录后 cookie 也视为鉴权通过
                if (!ok)
                {
                    var sessionCookie = context.Request.Cookies["catclaw_session"];
                    ok = !string.IsNullOrEmpty(sessionCookie) && sessionCookie == auth.AccessToken;
                }
            }

            if (!ok)
            {
                context.Response.StatusCode = 401;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync("Unauthorized");
                return;
            }
        }

        await _next(context);
    }
}
