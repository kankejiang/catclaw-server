using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.Subsonic;

public partial class SubsonicController
{
    // GET /rest/getPlayQueue.view
    [HttpGet("getPlayQueue.view")]
    public async Task<IActionResult> GetPlayQueue()
    {
        var user = await ResolveSubsonicUser();
        if (user == null) return SubsonicError("Unauthorized", 40);

        var pq = await _db.PlayQueues.FirstOrDefaultAsync(p => p.UserId == user.Id);

        if (pq == null)
        {
            return SubsonicOk(new Dictionary<string, object>
            {
                ["playQueue"] = new Dictionary<string, object>
                {
                    ["entry"] = Array.Empty<object>(),
                    ["current"] = 0,
                    ["position"] = 0
                }
            });
        }

        var songIds = System.Text.Json.JsonSerializer.Deserialize<long[]>(pq.SongIds) ?? Array.Empty<long>();
        var songs = await _db.Songs
            .Include(s => s.Artist)
            .Include(s => s.Album)
            .Where(s => songIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id);

        var entries = songIds
            .Where(id => songs.ContainsKey(id))
            .Select(id => SongToDict(songs[id]))
            .ToArray();

        var currentSongId = songIds.Length > pq.CurrentIndex && pq.CurrentIndex >= 0
            ? songIds[pq.CurrentIndex]
            : 0;

        return SubsonicOk(new Dictionary<string, object>
        {
            ["playQueue"] = new Dictionary<string, object>
            {
                ["entry"] = entries,
                ["current"] = currentSongId.ToString(),
                ["position"] = pq.PositionMs,
                ["username"] = user.Username,
                ["changed"] = pq.UpdatedAt.ToString("o"),
                ["changedBy"] = "CatClawMusic"
            }
        });
    }

    // GET /rest/savePlayQueue.view
    [HttpGet("savePlayQueue.view")]
    public async Task<IActionResult> SavePlayQueue(
        [FromQuery(Name = "id")] List<string>? ids,
        [FromQuery] string? current,
        [FromQuery] long position = 0)
    {
        var user = await ResolveSubsonicUser();
        if (user == null) return SubsonicError("Unauthorized", 40);

        var songIds = ids?
            .Where(id => long.TryParse(id, out _))
            .Select(long.Parse)
            .ToArray() ?? Array.Empty<long>();

        var currentIndex = 0;
        if (!string.IsNullOrEmpty(current) && long.TryParse(current, out var currentId))
        {
            currentIndex = Array.IndexOf(songIds, currentId);
            if (currentIndex < 0) currentIndex = 0;
        }

        var existing = await _db.PlayQueues.FirstOrDefaultAsync(p => p.UserId == user.Id);
        if (existing != null)
        {
            existing.SongIds = System.Text.Json.JsonSerializer.Serialize(songIds);
            existing.CurrentIndex = currentIndex;
            existing.PositionMs = position;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.PlayQueues.Add(new PlayQueue
            {
                UserId = user.Id,
                SongIds = System.Text.Json.JsonSerializer.Serialize(songIds),
                CurrentIndex = currentIndex,
                PositionMs = position
            });
        }

        await _db.SaveChangesAsync();
        return SubsonicOk(new Dictionary<string, object>());
    }

    // GET /rest/getBookmarks.view
    [HttpGet("getBookmarks.view")]
    public IActionResult GetBookmarks()
    {
        // Bookmarks not yet implemented — return empty
        return SubsonicOk(new Dictionary<string, object>
        {
            ["bookmarks"] = new Dictionary<string, object>
            {
                ["bookmark"] = Array.Empty<object>()
            }
        });
    }
}
