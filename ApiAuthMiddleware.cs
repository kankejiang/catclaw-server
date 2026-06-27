using System.Security.Cryptography;
using System.Text;

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

    public async Task InvokeAsync(HttpContext context, ServerAuthOptions auth)
    {
        var path = context.Request.Path.Value ?? "";

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
                    ok = authHeader["Bearer ".Length..].Trim() == auth.AccessToken;
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
