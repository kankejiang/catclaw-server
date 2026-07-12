using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.Subsonic;

public partial class SubsonicController
{
    // GET /rest/star.view
    [HttpGet("star.view")]
    public async Task<IActionResult> Star([FromQuery] string? id, [FromQuery] string? albumId, [FromQuery] string? artistId)
    {
        var user = await ResolveSubsonicUser();
        if (user == null) return SubsonicError("Unauthorized", 40);

        if (!string.IsNullOrEmpty(id) && long.TryParse(id, out var songId))
        {
            if (!await _db.Favorites.AnyAsync(f => f.UserId == user.Id && f.SongId == songId))
            {
                _db.Favorites.Add(new Favorite { UserId = user.Id, SongId = songId });
                await _db.SaveChangesAsync();
            }
        }

        return SubsonicOk(new Dictionary<string, object>());
    }

    // GET /rest/unstar.view
    [HttpGet("unstar.view")]
    public async Task<IActionResult> Unstar([FromQuery] string? id, [FromQuery] string? albumId, [FromQuery] string? artistId)
    {
        var user = await ResolveSubsonicUser();
        if (user == null) return SubsonicError("Unauthorized", 40);

        if (!string.IsNullOrEmpty(id) && long.TryParse(id, out var songId))
        {
            var fav = await _db.Favorites.FirstOrDefaultAsync(f => f.UserId == user.Id && f.SongId == songId);
            if (fav != null)
            {
                _db.Favorites.Remove(fav);
                await _db.SaveChangesAsync();
            }
        }

        return SubsonicOk(new Dictionary<string, object>());
    }

    // GET /rest/setRating.view
    [HttpGet("setRating.view")]
    public async Task<IActionResult> SetRating([FromQuery] string id, [FromQuery] int rating)
    {
        var user = await ResolveSubsonicUser();
        if (user == null) return SubsonicError("Unauthorized", 40);

        if (!long.TryParse(id, out var songId))
            return SubsonicError("Invalid id", 10);

        if (rating < 0 || rating > 5)
            return SubsonicError("Rating must be between 0 and 5", 10);

        var existing = await _db.Ratings.FirstOrDefaultAsync(r => r.UserId == user.Id && r.SongId == songId);

        if (rating == 0)
        {
            if (existing != null)
            {
                _db.Ratings.Remove(existing);
                await _db.SaveChangesAsync();
            }
        }
        else
        {
            if (existing != null)
            {
                existing.Score = rating;
            }
            else
            {
                _db.Ratings.Add(new Rating { UserId = user.Id, SongId = songId, Score = rating });
            }
            await _db.SaveChangesAsync();
        }

        return SubsonicOk(new Dictionary<string, object>());
    }

    // GET /rest/scrobble.view
    [HttpGet("scrobble.view")]
    public async Task<IActionResult> Scrobble([FromQuery] string id, [FromQuery] long? time, [FromQuery] bool submission = true)
    {
        var user = await ResolveSubsonicUser();
        if (user == null) return SubsonicError("Unauthorized", 40);

        if (!long.TryParse(id, out var songId))
            return SubsonicError("Invalid id", 10);

        if (submission)
        {
            _db.Scrobbles.Add(new Scrobble
            {
                UserId = user.Id,
                SongId = songId,
                Timestamp = time.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(time.Value).UtcDateTime : DateTime.UtcNow,
                DurationPlayedMs = 0 // Subsonic doesn't provide duration in scrobble
            });
            await _db.SaveChangesAsync();
        }

        return SubsonicOk(new Dictionary<string, object>());
    }
}
