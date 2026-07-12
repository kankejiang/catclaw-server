using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace CatClawMusicServer.ClawCircle.Transfer;

/// <summary>
/// P2P 分块传输引擎 — 支持 SHA256 校验、断点续传、并行下载。
/// 每个传输任务将文件分割为固定大小的块，独立校验和传输。
/// </summary>
public class TransferEngine
{
    public const int ChunkSize = 256 * 1024; // 256KB per chunk
    private readonly ConcurrentDictionary<string, TransferTask> _tasks = new();
    private readonly ILogger<TransferEngine> _logger;

    public TransferEngine(ILogger<TransferEngine> logger) => _logger = logger;

    /// <summary>创建文件分片清单（发送方调用）</summary>
    public async Task<PieceManifest> CreateManifestAsync(string filePath)
    {
        var fi = new FileInfo(filePath);
        if (!fi.Exists) throw new FileNotFoundException(filePath);

        var totalChunks = (int)Math.Ceiling((double)fi.Length / ChunkSize);
        var chunks = new List<ChunkInfo>(totalChunks);

        using var fs = fi.OpenRead();
        var buffer = new byte[ChunkSize];
        int index = 0;

        while (true)
        {
            var bytesRead = await fs.ReadAsync(buffer.AsMemory());
            if (bytesRead == 0) break;

            var hash = SHA256.HashData(buffer.AsSpan(0, bytesRead));
            chunks.Add(new ChunkInfo
            {
                Index = index++,
                Size = bytesRead,
                Sha256 = Convert.ToHexString(hash).ToLowerInvariant()
            });
        }

        return new PieceManifest
        {
            FileName = fi.Name,
            TotalSize = fi.Length,
            ChunkSize = ChunkSize,
            TotalChunks = totalChunks,
            Chunks = chunks,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>创建接收任务（接收方调用）</summary>
    public TransferTask CreateReceiveTask(PieceManifest manifest, string outputDir)
    {
        var taskId = Guid.NewGuid().ToString("N")[..12];
        var task = new TransferTask
        {
            Id = taskId,
            Manifest = manifest,
            OutputDir = outputDir,
            OutputFile = Path.Combine(outputDir, manifest.FileName),
            ReceivedChunks = new bool[manifest.TotalChunks],
            Status = TransferStatus.Created
        };

        Directory.CreateDirectory(outputDir);
        _tasks[taskId] = task;
        return task;
    }

    /// <summary>接收一个数据块并校验</summary>
    public bool ReceiveChunk(string taskId, int chunkIndex, byte[] data)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
            return false;

        if (chunkIndex < 0 || chunkIndex >= task.Manifest.TotalChunks)
            return false;

        // SHA256 校验
        var expectedHash = task.Manifest.Chunks[chunkIndex].Sha256;
        var actualHash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        if (expectedHash != actualHash)
        {
            _logger.LogWarning("块 {Index} SHA256 校验失败: expected={Expected}, actual={Actual}",
                chunkIndex, expectedHash, actualHash);
            return false;
        }

        // 写入文件
        var offset = (long)chunkIndex * ChunkSize;
        using var fs = new FileStream(task.OutputFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        fs.Seek(offset, SeekOrigin.Begin);
        fs.Write(data, 0, data.Length);

        task.ReceivedChunks[chunkIndex] = true;
        task.ReceivedCount++;

        if (task.ReceivedCount >= task.Manifest.TotalChunks)
            task.Status = TransferStatus.Complete;

        return true;
    }

    /// <summary>获取缺失的块索引列表（用于断点续传）</summary>
    public int[] GetMissingChunks(string taskId)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
            return Array.Empty<int>();

        var missing = new List<int>();
        for (int i = 0; i < task.ReceivedChunks.Length; i++)
        {
            if (!task.ReceivedChunks[i]) missing.Add(i);
        }
        return missing.ToArray();
    }

    /// <summary>读取一个块的数据（发送方调用）</summary>
    public async Task<byte[]?> ReadChunkAsync(string filePath, int chunkIndex)
    {
        var offset = (long)chunkIndex * ChunkSize;
        var fi = new FileInfo(filePath);
        if (!fi.Exists || offset >= fi.Length) return null;

        var size = (int)Math.Min(ChunkSize, fi.Length - offset);
        var buffer = new byte[size];

        using var fs = fi.OpenRead();
        fs.Seek(offset, SeekOrigin.Begin);
        await fs.ReadAsync(buffer.AsMemory());
        return buffer;
    }

    /// <summary>获取传输任务</summary>
    public TransferTask? GetTask(string taskId)
        => _tasks.TryGetValue(taskId, out var task) ? task : null;

    /// <summary>清理已完成/过期的任务</summary>
    public void Cleanup(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        foreach (var kv in _tasks)
        {
            if (kv.Value.CreatedAt < cutoff && kv.Value.Status != TransferStatus.InProgress)
                _tasks.TryRemove(kv.Key, out _);
        }
    }
}

// ── 传输模型 ──

public enum TransferStatus
{
    Created,
    InProgress,
    Complete,
    Failed
}

public class TransferTask
{
    public string Id { get; set; } = "";
    public PieceManifest Manifest { get; set; } = new();
    public string OutputDir { get; set; } = "";
    public string OutputFile { get; set; } = "";
    public bool[] ReceivedChunks { get; set; } = Array.Empty<bool>();
    public int ReceivedCount { get; set; }
    public TransferStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public double Progress => Manifest.TotalChunks > 0
        ? (double)ReceivedCount / Manifest.TotalChunks
        : 0;
}

/// <summary>文件分片清单 — 描述一个文件的分块信息和校验值。</summary>
public class PieceManifest
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("totalSize")]
    public long TotalSize { get; set; }

    [JsonPropertyName("chunkSize")]
    public int ChunkSize { get; set; }

    [JsonPropertyName("totalChunks")]
    public int TotalChunks { get; set; }

    [JsonPropertyName("chunks")]
    public List<ChunkInfo> Chunks { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}

public class ChunkInfo
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";
}
