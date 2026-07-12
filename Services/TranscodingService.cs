using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace CatClawMusicServer.Services;

/// <summary>
/// FFmpeg 转码服务 — 参考 Jellyfin 的 TranscodeManager 模式。
/// 管理 FFmpeg 进程生命周期、HLS 分片生成、LRU 缓存淘汰。
/// </summary>
public class TranscodingService : IDisposable
{
    private readonly StreamingOptions _opts;
    private readonly ILogger<TranscodingService> _logger;
    private readonly ConcurrentDictionary<string, TranscodingJob> _jobs = new();
    private readonly SemaphoreSlim _globalThrottle;
    private readonly Timer _cleanupTimer;
    private readonly Timer _idleKillTimer;

    public TranscodingService(StreamingOptions opts, ILogger<TranscodingService> logger)
    {
        _opts = opts;
        _logger = logger;
        _globalThrottle = new SemaphoreSlim(opts.MaxConcurrentTranscodes, opts.MaxConcurrentTranscodes);
        Directory.CreateDirectory(opts.TranscodeDir);

        // LRU 清理定时器：每 10 分钟
        _cleanupTimer = new Timer(_ => RunCacheCleanup(), null,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));

        // 空闲进程清理：每 30 秒检查
        _idleKillTimer = new Timer(_ => KillIdleJobs(), null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>获取或启动 HLS 转码任务</summary>
    public async Task<TranscodingJob> GetOrStartHlsJobAsync(long songId, string filePath, int bitrate, CancellationToken ct)
    {
        var key = $"{songId}_{bitrate}";

        if (_jobs.TryGetValue(key, out var existing) && existing.IsRunning)
        {
            existing.LastAccessedAt = DateTime.UtcNow;
            return existing;
        }

        // 全局并发控制
        await _globalThrottle.WaitAsync(ct);
        try
        {
            // 双重检查
            if (_jobs.TryGetValue(key, out existing) && existing.IsRunning)
            {
                existing.LastAccessedAt = DateTime.UtcNow;
                return existing;
            }

            var outputDir = Path.Combine(_opts.TranscodeDir, songId.ToString(), bitrate.ToString());
            Directory.CreateDirectory(outputDir);

            var job = new TranscodingJob
            {
                Key = key,
                SongId = songId,
                Bitrate = bitrate,
                OutputDir = outputDir
            };

            // 如果分片已全部生成（完整转码），直接返回
            var indexFile = Path.Combine(outputDir, "index.m3u8");
            if (File.Exists(indexFile) && IsTranscodeComplete(indexFile))
            {
                job.Status = TranscodeStatus.Complete;
                job.LastAccessedAt = DateTime.UtcNow;
                _jobs[key] = job;
                return job;
            }

            // 启动 FFmpeg
            StartFFmpegHls(job, filePath, bitrate, ct);
            _jobs[key] = job;
            return job;
        }
        catch
        {
            _globalThrottle.Release();
            throw;
        }
    }

    /// <summary>获取或启动直出转码（非 HLS，单文件转码到 stdout）</summary>
    public async Task<TranscodingJob> GetOrStartStreamJobAsync(long songId, string filePath, string format, int bitrate, CancellationToken ct)
    {
        var key = $"{songId}_stream_{format}_{bitrate}";

        if (_jobs.TryGetValue(key, out var existing) && existing.IsRunning)
        {
            existing.LastAccessedAt = DateTime.UtcNow;
            return existing;
        }

        await _globalThrottle.WaitAsync(ct);
        try
        {
            if (_jobs.TryGetValue(key, out existing) && existing.IsRunning)
            {
                existing.LastAccessedAt = DateTime.UtcNow;
                return existing;
            }

            var job = new TranscodingJob
            {
                Key = key,
                SongId = songId,
                Bitrate = bitrate,
                OutputDir = ""
            };

            StartFFmpegStream(job, filePath, format, bitrate, ct);
            _jobs[key] = job;
            return job;
        }
        catch
        {
            _globalThrottle.Release();
            throw;
        }
    }

    /// <summary>等待分片生成（轮询直到至少 minSegments 个分片就绪）</summary>
    public async Task<bool> WaitForSegmentsAsync(string outputDir, int minSegments, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < _opts.SegmentWaitTimeoutMs)
        {
            ct.ThrowIfCancellationRequested();

            var indexFile = Path.Combine(outputDir, "index.m3u8");
            if (File.Exists(indexFile))
            {
                var lines = await File.ReadAllLinesAsync(indexFile, ct);
                var segmentCount = lines.Count(l => l.EndsWith(".aac", StringComparison.OrdinalIgnoreCase)
                    || l.EndsWith(".ts", StringComparison.OrdinalIgnoreCase));
                if (segmentCount >= minSegments)
                    return true;
            }

            await Task.Delay(200, ct);
        }
        return false;
    }

    /// <summary>生成 Master Playlist</summary>
    public string BuildMasterPlaylist(int songBitrate, bool isLossless, int[]? bitrates = null)
    {
        var brs = bitrates ?? _opts.DefaultBitrates;
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine("#EXT-X-VERSION:3");

        foreach (var br in brs.OrderBy(b => b))
        {
            // 跳过高于源码码率的档位
            if (br > songBitrate / 1000 && songBitrate > 0) continue;

            var codec = br <= 96 ? "mp4a.40.5" : "mp4a.40.2"; // AAC-HE vs AAC-LC
            sb.AppendLine($"#EXT-X-STREAM-INF:BANDWIDTH={br * 1000},CODECS=\"{codec}\"");
            sb.AppendLine($"{br}/index.m3u8");
        }

        // 无损直出
        if (isLossless)
        {
            sb.AppendLine($"#EXT-X-STREAM-INF:BANDWIDTH={Math.Max(songBitrate, 1411000)}");
            sb.AppendLine("original/index.m3u8");
        }

        return sb.ToString();
    }

    /// <summary>生成原始文件的直通 Playlist（无损直出用）</summary>
    public string BuildOriginalPlaylist(string filePath, int durationSec)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine("#EXT-X-VERSION:3");
        sb.AppendLine($"#EXT-X-TARGETDURATION:{durationSec + 1}");
        sb.AppendLine("#EXT-X-MEDIA-SEQUENCE:0");
        sb.AppendLine($"#EXTINF:{durationSec}.000,");
        // 返回相对路径，实际由 Controller 处理文件读取
        sb.AppendLine("original");
        sb.AppendLine("#EXT-X-ENDLIST");
        return sb.ToString();
    }

    /// <summary>获取转码缓存目录大小</summary>
    public long GetCacheSizeBytes()
    {
        if (!Directory.Exists(_opts.TranscodeDir)) return 0;
        return Directory.GetFiles(_opts.TranscodeDir, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);
    }

    /// <summary>清理全部转码缓存</summary>
    public void ClearCache()
    {
        // 先停掉所有正在运行的任务
        foreach (var job in _jobs.Values)
            KillJob(job);

        _jobs.Clear();

        if (Directory.Exists(_opts.TranscodeDir))
        {
            try { Directory.Delete(_opts.TranscodeDir, true); }
            catch (Exception ex) { _logger.LogWarning(ex, "清理转码缓存失败"); }
        }
        Directory.CreateDirectory(_opts.TranscodeDir);
    }

    // ── 内部方法 ──

    private void StartFFmpegHls(TranscodingJob job, string filePath, int bitrate, CancellationToken ct)
    {
        var segmentTime = _opts.SegmentDurationSeconds;
        var outputPattern = Path.Combine(job.OutputDir, "%d.aac");
        var indexFile = Path.Combine(job.OutputDir, "index.m3u8");

        var args = $"-hide_banner -loglevel warning " +
                   $"-i \"{filePath}\" " +
                   $"-c:a aac -b:a {bitrate}k -ar 44100 " +
                   $"-f hls -hls_time {segmentTime} -hls_list_size 0 " +
                   $"-hls_segment_filename \"{outputPattern}\" " +
                   $"-hls_flags independent_segments " +
                   $"\"{indexFile}\"";

        var psi = new ProcessStartInfo
        {
            FileName = _opts.FFmpegPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stderr = new StringBuilder();

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) stderr.AppendLine(e.Data);
        };

        process.Exited += (_, _) =>
        {
            job.Status = process.ExitCode == 0 ? TranscodeStatus.Complete : TranscodeStatus.Error;
            job.ExitCode = process.ExitCode;
            if (process.ExitCode != 0)
                _logger.LogWarning("FFmpeg HLS 退出码 {Code}: {Stderr}", process.ExitCode, stderr.ToString());
            try { _globalThrottle.Release(); } catch { /* 已释放过 */ }
        };

        process.Start();
        process.BeginErrorReadLine();

        job.Process = process;
        job.Status = TranscodeStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        job.LastAccessedAt = DateTime.UtcNow;

        ct.Register(() => KillJob(job));
    }

    private void StartFFmpegStream(TranscodingJob job, string filePath, string format, int bitrate, CancellationToken ct)
    {
        var codec = format switch
        {
            "opus" => "-c:a libopus",
            "mp3" => "-c:a libmp3lame",
            _ => "-c:a aac"
        };

        var args = $"-hide_banner -loglevel warning " +
                   $"-i \"{filePath}\" " +
                   $"{codec} -b:a {bitrate}k -ar 44100 " +
                   $"-f {GetContainerFormat(format)} pipe:1";

        var psi = new ProcessStartInfo
        {
            FileName = _opts.FFmpegPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.Exited += (_, _) =>
        {
            job.Status = TranscodeStatus.Complete;
            try { _globalThrottle.Release(); } catch { }
        };

        process.Start();
        process.BeginErrorReadLine();

        job.Process = process;
        job.Status = TranscodeStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        job.LastAccessedAt = DateTime.UtcNow;

        ct.Register(() => KillJob(job));
    }

    private static string GetContainerFormat(string codec) => codec switch
    {
        "opus" => "opus",
        "mp3" => "mp3",
        _ => "adts"
    };

    private void KillJob(TranscodingJob job)
    {
        if (job.Process == null || job.Process.HasExited) return;
        try
        {
            // 优雅终止：向 stdin 发送 'q'
            job.Process.StandardInput.WriteLine("q");
            if (!job.Process.WaitForExit(3000))
                job.Process.Kill();
        }
        catch { /* 进程可能已退出 */ }
    }

    private void KillIdleJobs()
    {
        var now = DateTime.UtcNow;
        foreach (var job in _jobs.Values)
        {
            if (job.IsRunning && (now - job.LastAccessedAt).TotalSeconds > _opts.IdleKillSeconds)
            {
                _logger.LogInformation("清理空闲转码任务: {Key}", job.Key);
                KillJob(job);
                _jobs.TryRemove(job.Key, out _);
            }
        }
    }

    private void RunCacheCleanup()
    {
        var maxSizeBytes = (long)_opts.TranscodeCacheSizeGB * 1024 * 1024 * 1024;
        var currentSize = GetCacheSizeBytes();

        if (currentSize <= maxSizeBytes) return;

        _logger.LogInformation("转码缓存超限 ({Current}/{Max} GB)，开始 LRU 清理",
            currentSize / 1024.0 / 1024.0 / 1024.0, _opts.TranscodeCacheSizeGB);

        // 按最后访问时间排序，删除最旧的文件
        var files = Directory.GetFiles(_opts.TranscodeDir, "*", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f))
            .OrderBy(f => f.LastAccessTimeUtc)
            .ToList();

        foreach (var file in files)
        {
            if (currentSize <= maxSizeBytes * 0.8) break; // 清理到 80%
            try
            {
                var size = file.Length;
                file.Delete();
                currentSize -= size;
            }
            catch { /* 文件可能正在被 FFmpeg 使用 */ }
        }
    }

    private static bool IsTranscodeComplete(string indexFile)
    {
        try
        {
            var content = File.ReadAllText(indexFile);
            return content.Contains("#EXT-X-ENDLIST");
        }
        catch { return false; }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
        _idleKillTimer.Dispose();

        foreach (var job in _jobs.Values)
            KillJob(job);

        _jobs.Clear();
        _globalThrottle.Dispose();
    }
}

// ── 转码任务状态 ──

public class TranscodingJob
{
    public string Key { get; set; } = "";
    public long SongId { get; set; }
    public int Bitrate { get; set; }
    public string OutputDir { get; set; } = "";
    public Process? Process { get; set; }
    public TranscodeStatus Status { get; set; } = TranscodeStatus.Pending;
    public int ExitCode { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime LastAccessedAt { get; set; }

    public bool IsRunning => Status == TranscodeStatus.Running
        && Process != null && !Process.HasExited;
}

public enum TranscodeStatus
{
    Pending,
    Running,
    Complete,
    Error
}
