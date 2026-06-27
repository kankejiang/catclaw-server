namespace CatClawMusicServer;

/// <summary>
/// 服务端鉴权配置（内网/NAS 部署时用于防止局域网未授权访问）。
/// AccessToken 为空表示关闭鉴权（本地开发）。
/// </summary>
public record ServerAuthOptions
{
    /// <summary>访问令牌。Subsonic 端点作为 password 使用；/api 端点作为 Bearer token 使用。</summary>
    public string AccessToken { get; init; } = "";
}
