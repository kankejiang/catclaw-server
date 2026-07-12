using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Services;

public class MusicScanner
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ILogger<MusicScanner> _logger;
    private static readonly string[] SupportedExtensions =
    {
        ".mp3", ".flac", ".wav", ".wma", ".ogg",
        ".aiff", ".m4a", ".ape", ".wv", ".mp4",
        ".mp2", ".mpc", ".tta", ".opus"
    };

    public MusicScanner(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<MusicScanner> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// 扫描目录并将音乐信息存入数据库
    /// </summary>
    public async Task<ScanResult> ScanDirectoryAsync(string directory, string coverOutputDir, CancellationToken ct = default)
    {
        var result = new ScanResult();
        if (!Directory.Exists(directory))
        {
            _logger.LogWarning("目录不存在: {Dir}", directory);
            return result;
        }

        using var db = _dbFactory.CreateDbContext();

        _logger.LogInformation("开始扫描目录: {Dir}", directory);
        var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

        foreach (var filePath in files)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await ProcessFileAsync(db, filePath, coverOutputDir, result, ct);
                result.ProcessedCount++;

                // 每 100 个文件保存一次，避免大事务
                if (result.ProcessedCount % 100 == 0)
                    await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理文件失败: {File}", filePath);
                result.ErrorCount++;
            }
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation("扫描完成: 处理 {Processed} 个文件, 新增 {Added}, 更新 {Updated}, 错误 {Errors}",
            result.ProcessedCount, result.AddedCount, result.UpdatedCount, result.ErrorCount);

        return result;
    }

    /// <summary>增量扫描 — 仅检查新增/修改的文件（基于 FileHash）</summary>
    public async Task<ScanResult> IncrementalScanAsync(string directory, string coverOutputDir, CancellationToken ct = default)
    {
        var result = new ScanResult();
        if (!Directory.Exists(directory))
        {
            _logger.LogWarning("目录不存在: {Dir}", directory);
            return result;
        }

        using var db = _dbFactory.CreateDbContext();

        _logger.LogInformation("开始增量扫描目录: {Dir}", directory);
        var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

        foreach (var filePath in files)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // 计算文件哈希
                var fileHash = ComputeFileHash(filePath);
                var existing = await db.Songs.FirstOrDefaultAsync(s => s.FilePath == filePath, ct);

                // 如果文件存在且哈希匹配，跳过
                if (existing != null && existing.FileHash == fileHash)
                {
                    result.SkippedCount++;
                    continue;
                }

                await ProcessFileAsync(db, filePath, coverOutputDir, result, ct, fileHash);
                result.ProcessedCount++;

                if (result.ProcessedCount % 100 == 0)
                    await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理文件失败: {File}", filePath);
                result.ErrorCount++;
            }
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation("增量扫描完成: 处理 {Processed}, 新增 {Added}, 更新 {Updated}, 跳过 {Skipped}, 错误 {Errors}",
            result.ProcessedCount, result.AddedCount, result.UpdatedCount, result.SkippedCount, result.ErrorCount);

        return result;
    }

    /// <summary>计算文件哈希（取前 64KB + 文件大小 + 修改时间）</summary>
    private static long ComputeFileHash(string filePath)
    {
        var fi = new FileInfo(filePath);
        var sample = Math.Min(fi.Length, 65536); // 前 64KB
        var bytes = new byte[sample];

        using var fs = fi.OpenRead();
        fs.Read(bytes, 0, (int)sample);

        // 简单哈希：文件长度 + 修改时间 + 前 64KB 的 FNV-1a
        long hash = fi.Length * 31 + fi.LastWriteTimeUtc.Ticks;
        foreach (var b in bytes)
            hash = (hash * 1099511628211L) ^ b;

        return hash;
    }

    private async Task ProcessFileAsync(ApplicationDbContext db, string filePath, string coverOutputDir, ScanResult result, CancellationToken ct, long? fileHash = null)
    {
        // 检查数据库中是否已存在该文件路径
        var existing = await db.Songs.FirstOrDefaultAsync(s => s.FilePath == filePath, ct);

        var tags = FileTagService.ReadTags(filePath, coverOutputDir);
        var fileInfo = new FileInfo(filePath);
        var lyricsPath = FileTagService.FindLyricsFile(filePath);
        var coverPath = tags?.CoverPath ?? FileTagService.FindCoverFile(filePath);

        // 计算文件哈希（如果未提供）
        fileHash ??= ComputeFileHash(filePath);

        // 确保 Artist 存在
        var artist = await EnsureArtistAsync(db, tags?.Artist ?? "未知艺术家", ct);
        // 确保 Album 存在
        var album = await EnsureAlbumAsync(db, tags?.Album ?? "未知专辑", artist.Id, coverPath, ct);

        if (existing == null)
        {
            // 新增
            var song = new Song
            {
                Title = tags?.Title ?? Path.GetFileNameWithoutExtension(filePath),
                ArtistId = artist.Id,
                AlbumId = album.Id,
                Duration = tags?.Duration ?? 0,
                FilePath = filePath,
                FileSize = fileInfo.Length,
                Bitrate = tags?.Bitrate ?? 0,
                TrackNumber = tags?.TrackNumber ?? 0,
                Year = tags?.Year ?? 0,
                Genre = tags?.Genre ?? "",
                DateAdded = DateTime.UtcNow,
                DateModified = fileInfo.LastWriteTimeUtc,
                CoverArtPath = coverPath,
                LyricsPath = lyricsPath,
                FileHash = fileHash
            };

            db.Songs.Add(song);
            result.AddedCount++;
        }
        else
        {
            // 更新（如果文件有修改）
            var lastWrite = fileInfo.LastWriteTimeUtc;
            if (lastWrite != existing.DateModified)
            {
                existing.Title = tags?.Title ?? Path.GetFileNameWithoutExtension(filePath);
                existing.ArtistId = artist.Id;
                existing.AlbumId = album.Id;
                existing.Duration = tags?.Duration ?? existing.Duration;
                existing.FileSize = fileInfo.Length;
                existing.Bitrate = tags?.Bitrate ?? existing.Bitrate;
                existing.TrackNumber = tags?.TrackNumber ?? existing.TrackNumber;
                existing.Year = tags?.Year ?? existing.Year;
                existing.Genre = tags?.Genre ?? existing.Genre;
                existing.DateModified = lastWrite;
                existing.CoverArtPath = coverPath;
                existing.LyricsPath = lyricsPath;
                existing.FileHash = fileHash;

                result.UpdatedCount++;
            }
            else
            {
                result.SkippedCount++;
            }
        }
    }

    private async Task<Artist> EnsureArtistAsync(ApplicationDbContext db, string name, CancellationToken ct)
    {
        var artist = await db.Artists.FirstOrDefaultAsync(a => a.Name == name, ct);
        if (artist == null)
        {
            artist = new Artist { Name = name };
            db.Artists.Add(artist);
            await db.SaveChangesAsync(ct);
        }
        return artist;
    }

    private async Task<Album> EnsureAlbumAsync(ApplicationDbContext db, string title, long artistId, string? coverPath, CancellationToken ct)
    {
        var album = await db.Albums.FirstOrDefaultAsync(a => a.Title == title && a.ArtistId == artistId, ct);
        if (album == null)
        {
            album = new Album { Title = title, ArtistId = artistId, Cover = coverPath };
            db.Albums.Add(album);
            await db.SaveChangesAsync(ct);
        }
        else if (!string.IsNullOrEmpty(coverPath) && string.IsNullOrEmpty(album.Cover))
        {
            album.Cover = coverPath;
            await db.SaveChangesAsync(ct);
        }
        return album;
    }
}

public class ScanResult
{
    public int ProcessedCount { get; set; }
    public int AddedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int ErrorCount { get; set; }
}
