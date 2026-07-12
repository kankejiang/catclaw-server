namespace CatClawMusicServer;

/// <summary>
/// 服务端鉴权配置（内网/NAS 部署时用于防止局域网未授权访问）。
/// AccessToken 为空表示关闭鉴权（本地开发）。
/// AdminPassword 为空表示 Web UI 免登（开发模式）。
/// </summary>
public record ServerAuthOptions
{
    /// <summary>访问令牌。Subsonic 端点作为 password 使用；/api 端点作为 Bearer token 使用。</summary>
    public string AccessToken { get; init; } = "";

    /// <summary>Web UI 管理员用户名。</summary>
    public string AdminUser { get; init; } = "admin";

    /// <summary>Web UI 管理员密码（空 = 跳过 Web UI 登录）。</summary>
    public string AdminPassword { get; init; } = "";
}
