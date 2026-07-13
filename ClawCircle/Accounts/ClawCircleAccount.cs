using System.ComponentModel.DataAnnotations;

namespace CatClawMusicServer.ClawCircle.Accounts;

/// <summary>
/// 猫爪驿站独立账号 — 跨设备共享积分的载体。
/// 与主服务器 User 实体分离，避免耦合，账号体系独立运作。
/// 密码使用 PBKDF2-SHA256 哈希存储（10 万次迭代 + 随机盐），防彩虹表和暴力破解。
/// </summary>
public class ClawCircleAccount
{
    public long Id { get; set; }

    /// <summary>登录用户名（唯一，3-20 字符）。</summary>
    [MaxLength(20)]
    public string Username { get; set; } = "";

    /// <summary>密码哈希（Base64，PBKDF2-SHA256）。</summary>
    [MaxLength(128)]
    public string PasswordHash { get; set; } = "";

    /// <summary>盐（Base64，16 字节随机）。</summary>
    [MaxLength(32)]
    public string Salt { get; set; } = "";

    /// <summary>昵称（显示用）。</summary>
    [MaxLength(30)]
    public string DisplayName { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>最近一次登录时间。</summary>
    public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;

    /// <summary>连续登录失败次数（达 5 次锁定 15 分钟）。</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>锁定截止时间（UTC），过期后自动解锁。</summary>
    public DateTime? LockedUntil { get; set; }

    // 导航属性
    public List<ClawCircleDevice> Devices { get; set; } = new();
}

/// <summary>
/// 猫爪驿站设备 — 一个账号可绑定多个设备（NAS、手机、PC），
/// 每个设备有独立 Token，共享账号积分。Token 泄露只需吊销该设备，不影响其他设备。
/// </summary>
public class ClawCircleDevice
{
    public long Id { get; set; }

    public long AccountId { get; set; }
    public ClawCircleAccount? Account { get; set; }

    /// <summary>设备 ID（客户端生成的唯一标识，用于 P2P 网络）。</summary>
    [MaxLength(64)]
    public string DeviceId { get; set; } = "";

    /// <summary>设备名称（如"NAS"、"安卓手机"）。</summary>
    [MaxLength(30)]
    public string DeviceName { get; set; } = "";

    /// <summary>访问 Token（SHA256 哈希存储，原始 token 仅登录时返回一次）。</summary>
    [MaxLength(64)]
    public string TokenHash { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>最近活跃时间（用于设备列表展示）。</summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>最近活跃的 IP（用于设备识别）。</summary>
    [MaxLength(45)]
    public string? LastIp { get; set; }
}
