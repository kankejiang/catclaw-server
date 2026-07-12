using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Services;

/// <summary>
/// 智能推荐服务 — 基于播放历史的推荐引擎，无需外部 ML 模型。
/// </summary>
public class RecommendService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ILogger<RecommendService> _logger;
    private readonly Random _random = new();

    public RecommendService(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<RecommendService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>每日推荐：混合策略生成推荐歌单</summary>
    public async Task<List<long>> GenerateDailyRecommendAsync(long userId, int count = 30)
    {
        using var db = _dbFactory.CreateDbContext();
        var since = DateTime.UtcNow.AddDays(-30);

        // 1. 获取用户最近 30 天听过的艺术家和流派偏好
        var topArtistIds = await db.Scrobbles
            .Where(s => s.UserId == userId && s.Timestamp >= since)
            .Include(s => s.Song)
            .GroupBy(s => s.Song.ArtistId)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => g.Key)
            .ToListAsync();

        var topGenres = await db.Scrobbles
            .Where(s => s.UserId == userId && s.Timestamp >= since && s.Song!.Genre != null && s.Song.Genre != "")
            .Include(s => s.Song)
            .GroupBy(s => s.Song!.Genre)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => g.Key!)
            .ToListAsync();

        // 用户已听过的歌曲
        var listenedIds = await db.Scrobbles
            .Where(s => s.UserId == userId)
            .Select(s => s.SongId)
            .Distinct()
            .ToListAsync();
        var listenedSet = new HashSet<long>(listenedIds);

        var result = new List<long>();
        var addedSet = new HashSet<long>();

        // 2. 60%: 常听艺术家的其他歌曲
        var artistSongCount = (int)(count * 0.6);
        if (topArtistIds.Count > 0)
        {
            var artistSongs = await db.Songs
                .Where(s => topArtistIds.Contains(s.ArtistId) && !listenedSet.Contains(s.Id))
                .Select(s => s.Id)
                .ToListAsync();

            Shuffle(artistSongs);
            foreach (var id in artistSongs.Take(artistSongCount))
            {
                if (addedSet.Add(id)) result.Add(id);
            }
        }

        // 3. 30%: 同流派未听过的歌曲
        var genreSongCount = (int)(count * 0.3);
        if (topGenres.Count > 0 && result.Count < count)
        {
            var genreSongs = await db.Songs
                .Where(s => topGenres.Contains(s.Genre) && !listenedSet.Contains(s.Id) && !addedSet.Contains(s.Id))
                .Select(s => s.Id)
                .ToListAsync();

            Shuffle(genreSongs);
            foreach (var id in genreSongs.Take(genreSongCount))
            {
                if (addedSet.Add(id)) result.Add(id);
            }
        }

        // 4. 10%: 完全随机（探索发现）
        if (result.Count < count)
        {
            var remaining = count - result.Count;
            var randomSongs = await db.Songs
                .Where(s => !listenedSet.Contains(s.Id) && !addedSet.Contains(s.Id))
                .Select(s => s.Id)
                .ToListAsync();

            Shuffle(randomSongs);
            foreach (var id in randomSongs.Take(remaining))
            {
                if (addedSet.Add(id)) result.Add(id);
            }
        }

        // 如果推荐不够，补充随机未听过的歌
        if (result.Count < count)
        {
            var filler = await db.Songs
                .Where(s => !addedSet.Contains(s.Id))
                .OrderBy(_ => Guid.NewGuid())
                .Take(count - result.Count)
                .Select(s => s.Id)
                .ToListAsync();
            result.AddRange(filler);
        }

        return result.Take(count).ToList();
    }

    /// <summary>最近播放（最多 50 首，去重）</summary>
    public async Task<List<long>> GetRecentlyPlayedAsync(long userId, int count = 50)
    {
        using var db = _dbFactory.CreateDbContext();

        return await db.Scrobbles
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.Timestamp)
            .Select(s => s.SongId)
            .Distinct()
            .Take(count)
            .ToListAsync();
    }

    /// <summary>最常播放（30 天窗口，最多 100 首）</summary>
    public async Task<List<long>> GetTopPlayedAsync(long userId, int count = 100, int days = 30)
    {
        using var db = _dbFactory.CreateDbContext();
        var since = DateTime.UtcNow.AddDays(-days);

        return await db.Scrobbles
            .Where(s => s.UserId == userId && s.Timestamp >= since)
            .GroupBy(s => s.SongId)
            .OrderByDescending(g => g.Count())
            .Take(count)
            .Select(g => g.Key)
            .ToListAsync();
    }

    /// <summary>随机发现 — 未听过或极少播放的歌曲</summary>
    public async Task<List<long>> GetDiscoverAsync(long userId, int count = 30)
    {
        using var db = _dbFactory.CreateDbContext();

        // 听过的歌曲
        var listenedIds = await db.Scrobbles
            .Where(s => s.UserId == userId)
            .GroupBy(s => s.SongId)
            .Where(g => g.Count() >= 3) // 听过 3 次以上才算"听过"
            .Select(g => g.Key)
            .ToListAsync();
        var listenedSet = new HashSet<long>(listenedIds);

        var allSongIds = await db.Songs.Select(s => s.Id).ToListAsync();
        var candidates = allSongIds.Where(id => !listenedSet.Contains(id)).ToList();
        Shuffle(candidates);
        return candidates.Take(count).ToList();
    }

    /// <summary>艺术家混搭 — 基于指定艺术家的相似推荐</summary>
    public async Task<List<long>> GetArtistMixAsync(long artistId, long userId, int count = 30)
    {
        using var db = _dbFactory.CreateDbContext();

        // 获取该艺术家的流派
        var genres = await db.Songs
            .Where(s => s.ArtistId == artistId && s.Genre != null && s.Genre != "")
            .Select(s => s.Genre)
            .Distinct()
            .Take(3)
            .ToListAsync();

        // 同流派的艺术家（排除当前艺术家）
        var similarArtistIds = await db.Songs
            .Where(s => genres.Contains(s.Genre) && s.ArtistId != artistId)
            .Select(s => s.ArtistId)
            .Distinct()
            .Take(5)
            .ToListAsync();

        var allArtistIds = new List<long> { artistId };
        allArtistIds.AddRange(similarArtistIds);

        var songs = await db.Songs
            .Where(s => allArtistIds.Contains(s.ArtistId))
            .Select(s => s.Id)
            .ToListAsync();

        Shuffle(songs);
        return songs.Take(count).ToList();
    }

    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
