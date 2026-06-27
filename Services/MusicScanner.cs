using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Services;

public class MusicScanner
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<MusicScanner> _logger;
    private static readonly string[] SupportedExtensions =
    {
        ".mp3", ".flac", ".wav", ".wma", ".ogg",
        ".aiff", ".m4a", ".ape", ".wv", ".mp4",
        ".mp2", ".mpc", ".tta", ".opus"
    };

    public MusicScanner(ApplicationDbContext db, ILogger<MusicScanner> logger)
    {
        _db = db;
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

        _logger.LogInformation("开始扫描目录: {Dir}", directory);
        var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

        foreach (var filePath in files)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await ProcessFileAsync(filePath, coverOutputDir, result, ct);
                result.ProcessedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理文件失败: {File}", filePath);
                result.ErrorCount++;
            }
        }

        _logger.LogInformation("扫描完成: 处理 {Processed} 个文件, 新增 {Added}, 更新 {Updated}, 错误 {Errors}",
            result.ProcessedCount, result.AddedCount, result.UpdatedCount, result.ErrorCount);

        return result;
    }

    private async Task ProcessFileAsync(string filePath, string coverOutputDir, ScanResult result, CancellationToken ct)
    {
        // 检查数据库中是否已存在该文件路径
        var existing = await _db.Songs.FirstOrDefaultAsync(s => s.FilePath == filePath, ct);

        var tags = FileTagService.ReadTags(filePath, coverOutputDir);
        var fileInfo = new FileInfo(filePath);
        var lyricsPath = FileTagService.FindLyricsFile(filePath);
        var coverPath = tags?.CoverPath ?? FileTagService.FindCoverFile(filePath);

        // 确保 Artist 存在
        var artist = await EnsureArtistAsync(tags?.Artist ?? "未知艺术家", ct);
        // 确保 Album 存在
        var album = await EnsureAlbumAsync(tags?.Album ?? "未知专辑", artist.Id, coverPath, ct);

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
                LyricsPath = lyricsPath
            };

            _db.Songs.Add(song);
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

                result.UpdatedCount++;
            }
            else
            {
                result.SkippedCount++;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<Artist> EnsureArtistAsync(string name, CancellationToken ct)
    {
        var artist = await _db.Artists.FirstOrDefaultAsync(a => a.Name == name, ct);
        if (artist == null)
        {
            artist = new Artist { Name = name };
            _db.Artists.Add(artist);
            await _db.SaveChangesAsync(ct);
        }
        return artist;
    }

    private async Task<Album> EnsureAlbumAsync(string title, long artistId, string? coverPath, CancellationToken ct)
    {
        var album = await _db.Albums.FirstOrDefaultAsync(a => a.Title == title && a.ArtistId == artistId, ct);
        if (album == null)
        {
            album = new Album { Title = title, ArtistId = artistId, Cover = coverPath };
            _db.Albums.Add(album);
            await _db.SaveChangesAsync(ct);
        }
        else if (!string.IsNullOrEmpty(coverPath) && string.IsNullOrEmpty(album.Cover))
        {
            album.Cover = coverPath;
            await _db.SaveChangesAsync(ct);
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
