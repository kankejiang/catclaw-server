namespace CatClawMusicServer;

/// <summary>
/// 保护静态 HTML 页面（含根路径 "/"，UseDefaultFiles 会将其解析为 index.html）。
/// 注册页仅未配置管理员时放行；已配置后访问注册页直接跳登录页。
/// 登录页、API/WebSocket、静态资源始终放行。
/// 重定向采用 HTTP 302 + No-Cache + meta refresh 三重保险，避免浏览器缓存导致不跳。
/// </summary>
public class WebUiAuthMiddleware
{
    private readonly RequestDelegate _next;

    public WebUiAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AdminCredentialStore store)
    {
        var path = context.Request.Path.Value ?? "";

        bool isLoginPage = path.EndsWith("/login.html", StringComparison.OrdinalIgnoreCase);
        bool isRegisterPage = path.EndsWith("/register.html", StringComparison.OrdinalIgnoreCase);

        // 注册页：未配置时放行，已配置 → 跳登录
        if (isRegisterPage)
        {
            if (!store.IsConfigured)
            {
                AddNoCacheHeaders(context);
                await _next(context);
            }
            else
            {
                await ForceRedirect(context, "/login.html");
            }
            return;
        }

        // 判断当前是否「需要保护的页面」（.html 文件 或 根路径）
        bool isPage = path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                      || path == "/"
                      || (path.EndsWith("/") && path.Length > 1);

        // 始终放行的路径
        bool isExempt =
               isLoginPage
            || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/rest/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/ws/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase);

        if (!isPage || isExempt)
        {
            if (isPage && isExempt)
                AddNoCacheHeaders(context);
            await _next(context);
            return;
        }

        // 管理员尚未注册 → 引导去注册页
        if (!store.IsConfigured)
        {
            await ForceRedirect(context, "/register.html");
            return;
        }

        // 已注册 → 检查登录 cookie
        var session = context.Request.Cookies["catclaw_session"];
        if (session == store.AccessToken)
        {
            AddNoCacheHeaders(context);
            await _next(context);
            return;
        }

        await ForceRedirect(context, "/login.html");
    }

    private static void AddNoCacheHeaders(HttpContext context)
    {
        context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "0";
    }

    private static async Task ForceRedirect(HttpContext context, string url)
    {
        context.Response.StatusCode = 302;
        AddNoCacheHeaders(context);
        context.Response.Headers["Location"] = url;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(
            $"<html><head><meta http-equiv=\"refresh\" content=\"0;url={url}\"></head>" +
            $"<body><a href=\"{url}\">正在跳转…</a></body></html>");
    }
}
