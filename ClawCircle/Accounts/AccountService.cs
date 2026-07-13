using System.Security.Cryptography;
using System.Text;
using CatClawMusicServer.Data;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.ClawCircle.Accounts;

/// <summary>
/// 猫爪驿站账号服务 — 注册/登录/Token 验证/设备管理。
///
/// 安全设计：
///   • 密码哈希：PBKDF2-SHA256，10 万次迭代 + 16 字节随机盐（防彩虹表）
///   • 防暴力破解：连续失败 5 次锁定 15 分钟
///   • Token：32 字节随机数，SHA256 哈希存储（原始 token 仅登录时返回一次）
///   • 多设备：每个设备独立 Token，泄露只需吊销该设备
///   • 修改密码：服务端完成，所有设备 Token 自动失效需重新登录
/// </summary>
public class AccountService
{
    private const int Pbkdf2Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    private readonly IDbContextFactory<ApplicationDbContext> _dbf;
    private readonly ILogger<AccountService> _logger;

    public AccountService(IDbContextFactory<ApplicationDbContext> dbf, ILogger<AccountService> logger)
    {
        _dbf = dbf;
        _logger = logger;
    }

    /// <summary>注册新账号。</summary>
    public async Task<(ClawCircleAccount? account, string? error)> RegisterAsync(string username, string password, string displayName = "")
    {
        username = username.Trim();
        if (username.Length < 3 || username.Length > 20)
            return (null, "用户名长度需 3-20 字符");
        if (password.Length < 6 || password.Length > 64)
            return (null, "密码长度需 6-64 字符");

        await using var db = await _dbf.CreateDbContextAsync();

        // 检查用户名是否已存在
        if (await db.ClawCircleAccounts.AnyAsync(a => a.Username == username))
            return (null, "用户名已存在");

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = PBKDF2(password, salt);

        var account = new ClawCircleAccount
        {
            Username = username,
            PasswordHash = Convert.ToBase64String(hash),
            Salt = Convert.ToBase64String(salt),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName.Trim(),
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow
        };

        db.ClawCircleAccounts.Add(account);
        await db.SaveChangesAsync();
        _logger.LogInformation("[account] 新账号注册: {Username} (id={Id})", username, account.Id);
        return (account, null);
    }

    /// <summary>登录并签发设备 Token。返回的 token 仅此一次可见。</summary>
    public async Task<(LoginResult? result, string? error)> LoginAsync(
        string username, string password, string deviceId, string deviceName, string? ip = null)
    {
        username = username.Trim();
        if (string.IsNullOrEmpty(deviceId))
            return (null, "需要 deviceId");

        await using var db = await _dbf.CreateDbContextAsync();
        var account = await db.ClawCircleAccounts.FirstOrDefaultAsync(a => a.Username == username);
        if (account == null)
            return (null, "用户名或密码错误");

        // 检查锁定状态
        if (account.LockedUntil != null && account.LockedUntil > DateTime.UtcNow)
        {
            var remaining = account.LockedUntil.Value - DateTime.UtcNow;
            return (null, $"账号已锁定，请 {Math.Ceiling(remaining.TotalMinutes)} 分钟后再试");
        }

        // 验证密码
        var salt = Convert.FromBase64String(account.Salt);
        var expectedHash = Convert.FromBase64String(account.PasswordHash);
        var actualHash = PBKDF2(password, salt);

        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
        {
            // 密码错误，累计失败次数
            account.FailedLoginCount++;
            if (account.FailedLoginCount >= MaxFailedAttempts)
            {
                account.LockedUntil = DateTime.UtcNow.Add(LockDuration);
                account.FailedLoginCount = 0;
                _logger.LogWarning("[account] 账号 {Username} 因连续 {N} 次密码错误锁定 15 分钟", username, MaxFailedAttempts);
            }
            await db.SaveChangesAsync();
            return (null, "用户名或密码错误");
        }

        // 登录成功，重置失败计数
        account.FailedLoginCount = 0;
        account.LockedUntil = null;
        account.LastLoginAt = DateTime.UtcNow;

        // 查找或创建设备记录
        var device = await db.ClawCircleDevices.FirstOrDefaultAsync(d => d.AccountId == account.Id && d.DeviceId == deviceId);
        var rawToken = GenerateToken();
        var tokenHash = HashToken(rawToken);

        if (device == null)
        {
            device = new ClawCircleDevice
            {
                AccountId = account.Id,
                DeviceId = deviceId,
                DeviceName = string.IsNullOrWhiteSpace(deviceName) ? deviceId : deviceName.Trim(),
                TokenHash = tokenHash,
                CreatedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
                LastIp = ip
            };
            db.ClawCircleDevices.Add(device);
        }
        else
        {
            device.TokenHash = tokenHash;
            device.DeviceName = string.IsNullOrWhiteSpace(deviceName) ? device.DeviceName : deviceName.Trim();
            device.LastSeenAt = DateTime.UtcNow;
            device.LastIp = ip;
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("[account] {Username} 设备 {DeviceName} 登录成功", username, device.DeviceName);

        return (new LoginResult
        {
            Token = rawToken,
            AccountId = account.Id,
            Username = account.Username,
            DisplayName = account.DisplayName,
            DeviceId = device.DeviceId,
            DeviceName = device.DeviceName
        }, null);
    }

    /// <summary>验证 Token，返回账号信息。无效返回 null。</summary>
    public async Task<ClawCircleAccount?> ValidateTokenAsync(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var tokenHash = HashToken(token);
        await using var db = await _dbf.CreateDbContextAsync();
        var device = await db.ClawCircleDevices
            .Include(d => d.Account)
            .FirstOrDefaultAsync(d => d.TokenHash == tokenHash);
        if (device == null) return null;

        // 更新活跃时间
        device.LastSeenAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return device.Account;
    }

    /// <summary>通过 deviceId 查找关联的账号（供账本记账使用）。</summary>
    public async Task<ClawCircleAccount?> FindByDeviceIdAsync(string deviceId)
    {
        await using var db = await _dbf.CreateDbContextAsync();
        var device = await db.ClawCircleDevices
            .Include(d => d.Account)
            .FirstOrDefaultAsync(d => d.DeviceId == deviceId);
        return device?.Account;
    }

    /// <summary>列出账号下所有设备。</summary>
    public async Task<List<ClawCircleDevice>> ListDevicesAsync(long accountId)
    {
        await using var db = await _dbf.CreateDbContextAsync();
        return await db.ClawCircleDevices
            .Where(d => d.AccountId == accountId)
            .OrderByDescending(d => d.LastSeenAt)
            .ToListAsync();
    }

    /// <summary>吊销指定设备 Token（退出登录）。</summary>
    public async Task<bool> RevokeDeviceAsync(long accountId, string deviceId)
    {
        await using var db = await _dbf.CreateDbContextAsync();
        var device = await db.ClawCircleDevices
            .FirstOrDefaultAsync(d => d.AccountId == accountId && d.DeviceId == deviceId);
        if (device == null) return false;
        db.ClawCircleDevices.Remove(device);
        await db.SaveChangesAsync();
        _logger.LogInformation("[account] 吊销设备 {DeviceId} (account={AccountId})", deviceId, accountId);
        return true;
    }

    /// <summary>修改密码（所有设备 Token 自动失效，需重新登录）。</summary>
    public async Task<(bool ok, string? error)> ChangePasswordAsync(long accountId, string oldPassword, string newPassword)
    {
        if (newPassword.Length < 6 || newPassword.Length > 64)
            return (false, "新密码长度需 6-64 字符");

        await using var db = await _dbf.CreateDbContextAsync();
        var account = await db.ClawCircleAccounts.FindAsync(accountId);
        if (account == null) return (false, "账号不存在");

        var salt = Convert.FromBase64String(account.Salt);
        var expectedHash = Convert.FromBase64String(account.PasswordHash);
        var actualHash = PBKDF2(oldPassword, salt);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
            return (false, "原密码错误");

        // 更新密码
        var newSalt = RandomNumberGenerator.GetBytes(SaltSize);
        var newHash = PBKDF2(newPassword, newSalt);
        account.PasswordHash = Convert.ToBase64String(newHash);
        account.Salt = Convert.ToBase64String(newSalt);

        // 删除所有设备 Token（强制重新登录）
        var devices = await db.ClawCircleDevices.Where(d => d.AccountId == accountId).ToListAsync();
        db.ClawCircleDevices.RemoveRange(devices);

        await db.SaveChangesAsync();
        _logger.LogInformation("[account] 账号 {Username} 修改密码，所有设备已退出", account.Username);
        return (true, null);
    }

    // ── 私有工具方法 ──

    private static byte[] PBKDF2(string password, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, HashSize);

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

/// <summary>登录成功返回结构。</summary>
public class LoginResult
{
    public string Token { get; set; } = "";
    public long AccountId { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
}
