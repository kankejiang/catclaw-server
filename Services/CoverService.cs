using System.Security.Cryptography;

namespace CatClawMusicServer.Services;

/// <summary>封面尺寸变体服务 — 生成并缓存 small/medium/large 缩略图。</summary>
public class CoverService
{
    private readonly string _cacheDir;
    private readonly ILogger<CoverService> _logger;

    public CoverService(ScannerOptions scannerOpts, ILogger<CoverService> logger)
    {
        _cacheDir = Path.Combine(scannerOpts.CoverOutputDir, "thumbs");
        Directory.CreateDirectory(_cacheDir);
        _logger = logger;
    }

    /// <summary>
    /// 获取指定尺寸的封面文件路径。若缓存不存在则返回原图路径。
    /// size: "small"(120) | "medium"(300) | "large"(600) | "original"
    /// </summary>
    public string? GetCoverPath(string? originalPath, string size)
    {
        if (string.IsNullOrEmpty(originalPath) || !File.Exists(originalPath))
            return null;

        if (size == "original" || string.IsNullOrEmpty(size))
            return originalPath;

        int targetSize = size switch
        {
            "small" => 120,
            "medium" => 300,
            "large" => 600,
            _ => 0
        };

        if (targetSize == 0) return originalPath;

        // 生成缓存文件名
        var hash = Convert.ToHexString(MD5.HashData(System.Text.Encoding.UTF8.GetBytes(originalPath)))[..16];
        var ext = Path.GetExtension(originalPath).ToLowerInvariant();
        var cachedPath = Path.Combine(_cacheDir, $"{hash}_{targetSize}{ext}");

        if (File.Exists(cachedPath))
            return cachedPath;

        // 尝试用 FFmpeg 生成缩略图（避免引入 System.Drawing 跨平台依赖）
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-hide_banner -loglevel error -i \"{originalPath}\" -vf scale={targetSize}:{targetSize} -frames:v 1 \"{cachedPath}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit(5000);
                if (File.Exists(cachedPath) && new FileInfo(cachedPath).Length > 0)
                    return cachedPath;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "封面缩略图生成失败: {Path}", originalPath);
        }

        // 回退到原图
        return originalPath;
    }
}
