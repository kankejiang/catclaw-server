using System.Text.Json;

namespace CatClawMusicServer;

/// <summary>
/// 管理员凭据持久化存储。首次启动：从 appsettings 读取默认值，AdminPassword 为空则视为「未配置」。
/// 注册后写入 Data/admin.json，后续重启优先读取文件（已配置则忽略 appsettings 默认值）。
/// 已注册后 AccessToken 仍用 appsettings 的值不变。
/// </summary>
public class AdminCredentialStore
{
    private readonly string _filePath;
    private readonly string _defaultAccessToken;

    private string _adminUser;
    private string _adminPassword;
    private bool _configured;

    public string AdminUser => _adminUser;
    public string AdminPassword => _configured ? _adminPassword : "";
    public string AccessToken => _defaultAccessToken;
    public bool IsConfigured => _configured;

    public AdminCredentialStore(ServerAuthOptions options)
    {
        _filePath = Path.Combine(AppContext.BaseDirectory, "Data", "admin.json");
        _defaultAccessToken = options.AccessToken;

        // 优先读已持久化的凭据
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var data = JsonSerializer.Deserialize<AdminData>(json);
                if (data != null && !string.IsNullOrEmpty(data.Password))
                {
                    _adminUser = data.User;
                    _adminPassword = data.Password;
                    _configured = true;
                    return;
                }
            }
        }
        catch { }

        // 回退到 appsettings 默认值
        _adminUser = options.AdminUser;
        _adminPassword = options.AdminPassword ?? "";
        _configured = !string.IsNullOrEmpty(_adminPassword);
    }

    /// <summary>注册管理员（仅首次/未配置时调用；已注册后拒绝覆盖）。</summary>
    public async Task<bool> TrySetCredentialsAsync(string user, string password)
    {
        if (_configured)
            return false;

        _adminUser = string.IsNullOrWhiteSpace(user) ? "admin" : user;
        _adminPassword = password;
        _configured = true;

        var data = new AdminData { User = _adminUser, Password = _adminPassword };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await File.WriteAllTextAsync(_filePath, JsonSerializer.Serialize(data));
        }
        catch { }
        return true;
    }

    private sealed class AdminData
    {
        public string User { get; set; } = "";
        public string Password { get; set; } = "";
    }
}
