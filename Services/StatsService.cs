using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Services;

/// <summary>
/// 播放统计服务 — 个人统计 + 全服统计 + 每日预聚合。
/// </summary>
public class StatsService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ILogger<StatsService> _logger;

    public StatsService(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<StatsService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>获取用户个人播放统计</summary>
    public async Task<UserStats> GetUserStatsAsync(long userId, int days = 30)
    {
        using var db = _dbFactory.CreateDbContext();
        var since = DateTime.UtcNow.AddDays(-days);

        var totalPlays = await db.Scrobbles
            .Where(s => s.UserId == userId && s.Timestamp >= since)
            .CountAsync();

        var totalDurationMs = await db.Scrobbles
            .Where(s => s.UserId == userId && s.Timestamp >= since)
            .SumAsync(s => (long?)s.DurationPlayedMs) ?? 0;

        // Top 10 artists
        var topArtists = await db.Scrobbles
            .Where(s => s.UserId == userId && s.Timestamp >= since)
            .Include(s => s.Song).ThenInclude(s => s!.Artist)
            .GroupBy(s => new { s.Song!.ArtistId, ArtistName = s.Song!.Artist!.Name })
            .Select(g => new ArtistStat
            {
                ArtistId = g.Key.ArtistId,
                ArtistName = g.Key.ArtistName,
                PlayCount = g.Count()
            })
            .OrderByDescending(a => a.PlayCount)
            .Take(10)
            .ToListAsync();

        // Top 10 songs
        var topSongs = await db.Scrobbles
            .Where(s => s.UserId == userId && s.Timestamp >= since)
            .Include(s => s.Song).ThenInclude(s => s!.Artist)
            .GroupBy(s => new { s.SongId, Title = s.Song!.Title, ArtistName = s.Song!.Artist!.Name })
            .Select(g => new SongStat
            {
                SongId = g.Key.SongId,
                Title = g.Key.Title,
                ArtistName = g.Key.ArtistName,
                PlayCount = g.Count()
            })
            .OrderByDescending(s => s.PlayCount)
            .Take(10)
            .ToListAsync();

        // 每日播放数 (最近 30 天)
        var dailyPlays = await db.Scrobbles
            .Where(s => s.UserId == userId && s.Timestamp >= since)
            .GroupBy(s => s.Timestamp.Date)
            .Select(g => new DailyPlay
            {
                Date = g.Key,
                Count = g.Count()
            })
            .OrderBy(d => d.Date)
            .ToListAsync();

        // 24 小时分布
        var hourlyDist = await db.Scrobbles
            .Where(s => s.UserId == userId && s.Timestamp >= since)
            .GroupBy(s => s.Timestamp.Hour)
            .Select(g => new HourlyCount
            {
                Hour = g.Key,
                Count = g.Count()
            })
            .OrderBy(h => h.Hour)
            .ToListAsync();

        // 流派分布
        var genreBreakdown = await db.Scrobbles
            .Where(s => s.UserId == userId && s.Timestamp >= since && s.Song!.Genre != null && s.Song.Genre != "")
            .Include(s => s.Song)
            .GroupBy(s => s.Song!.Genre)
            .Select(g => new GenreStat
            {
                Genre = g.Key!,
                Count = g.Count()
            })
            .OrderByDescending(g => g.Count)
            .Take(10)
            .ToListAsync();

        return new UserStats
        {
            TotalPlays = totalPlays,
            TotalDurationHours = Math.Round(totalDurationMs / 3600000.0, 1),
            TopArtists = topArtists,
            TopSongs = topSongs,
            DailyPlays = dailyPlays,
            HourlyDistribution = hourlyDist,
            GenreBreakdown = genreBreakdown
        };
    }

    /// <summary>全服统计（管理员用）</summary>
    public async Task<ServerStats> GetServerStatsAsync()
    {
        using var db = _dbFactory.CreateDbContext();

        var songCount = await db.Songs.CountAsync();
        var artistCount = await db.Artists.CountAsync();
        var albumCount = await db.Albums.CountAsync();
        var userCount = await db.Users.CountAsync();
        var totalPlays = await db.Scrobbles.CountAsync();
        var activeUsers = await db.Scrobbles
            .Where(s => s.Timestamp >= DateTime.UtcNow.AddDays(-7))
            .Select(s => s.UserId)
            .Distinct()
            .CountAsync();

        return new ServerStats
        {
            SongCount = songCount,
            ArtistCount = artistCount,
            AlbumCount = albumCount,
            UserCount = userCount,
            TotalPlays = totalPlays,
            ActiveUsersLast7Days = activeUsers
        };
    }

    /// <summary>聚合当日统计到 StatsDaily 表（后台定时调用）</summary>
    public async Task AggregateDailyStatsAsync()
    {
        using var db = _dbFactory.CreateDbContext();
        var today = DateTime.UtcNow.Date;

        var userStats = await db.Scrobbles
            .Where(s => s.Timestamp >= today)
            .GroupBy(s => s.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                PlayCount = g.Count(),
                TotalDurationMs = g.Sum(s => (long?)s.DurationPlayedMs) ?? 0
            })
            .ToListAsync();

        foreach (var stat in userStats)
        {
            var existing = await db.StatsDaily
                .FirstOrDefaultAsync(s => s.UserId == stat.UserId && s.Date == today);

            if (existing != null)
            {
                existing.PlayCount = stat.PlayCount;
                existing.TotalDurationMs = stat.TotalDurationMs;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                db.StatsDaily.Add(new StatsDaily
                {
                    UserId = stat.UserId,
                    Date = today,
                    PlayCount = stat.PlayCount,
                    TotalDurationMs = stat.TotalDurationMs
                });
            }
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("每日统计聚合完成: {Count} 个用户", userStats.Count);
    }
}

// ── 响应 DTO ──

public class UserStats
{
    public int TotalPlays { get; set; }
    public double TotalDurationHours { get; set; }
    public List<ArtistStat> TopArtists { get; set; } = new();
    public List<SongStat> TopSongs { get; set; } = new();
    public List<DailyPlay> DailyPlays { get; set; } = new();
    public List<HourlyCount> HourlyDistribution { get; set; } = new();
    public List<GenreStat> GenreBreakdown { get; set; } = new();
}

public class ArtistStat
{
    public long ArtistId { get; set; }
    public string ArtistName { get; set; } = "";
    public int PlayCount { get; set; }
}

public class SongStat
{
    public long SongId { get; set; }
    public string Title { get; set; } = "";
    public string ArtistName { get; set; } = "";
    public int PlayCount { get; set; }
}

public class DailyPlay
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}

public class HourlyCount
{
    public int Hour { get; set; }
    public int Count { get; set; }
}

public class GenreStat
{
    public string Genre { get; set; } = "";
    public int Count { get; set; }
}

public class ServerStats
{
    public int SongCount { get; set; }
    public int ArtistCount { get; set; }
    public int AlbumCount { get; set; }
    public int UserCount { get; set; }
    public int TotalPlays { get; set; }
    public int ActiveUsersLast7Days { get; set; }
}
