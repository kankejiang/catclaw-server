using CatClawMusicServer.Data;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer;

/// <summary>
/// 保护静态 HTML 页面。
/// 当数据库存在用户时（JWT 认证已启用），跳过此中间件 — 由 Vue SPA 前端处理 JWT 认证。
/// 仅当数据库无用户且管理员未配置时，引导到注册页。
/// </summary>
public class WebUiAuthMiddleware
{
    private readonly RequestDelegate _next;

    public WebUiAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AdminCredentialStore store, ApplicationDbContext db)
    {
        var path = context.Request.Path.Value ?? "";

        // ── 当数据库已有用户时，JWT 认证由 Vue SPA 处理，此中间件全部放行 ──
        try
        {
            var hasUsers = await db.Users.AnyAsync();
            if (hasUsers)
            {
                await _next(context);
                return;
            }
        }
        catch
        {
            // 数据库不存在或尚未迁移 — 继续走旧逻辑
        }

        bool isLoginPage = path.EndsWith("/login.html", StringComparison.OrdinalIgnoreCase);
        bool isRegisterPage = path.EndsWith("/register.html", StringComparison.OrdinalIgnoreCase);

        // register.html 已合并到 login.html，统一重定向
        if (isRegisterPage)
        {
            await ForceRedirect(context, "/login.html");
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

        // 管理员尚未注册 → 引导去登录页（登录页会自动显示注册表单）
        if (!store.IsConfigured)
        {
            await ForceRedirect(context, "/login.html");
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
