using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.Subsonic;

public partial class SubsonicController
{
    // GET /rest/getPlaylists.view
    [HttpGet("getPlaylists.view")]
    public async Task<IActionResult> GetPlaylists()
    {
        var user = await ResolveSubsonicUser();
        if (user == null) return SubsonicError("Unauthorized", 40);

        var playlists = await _db.Playlists
            .Include(p => p.Songs)
            .AsNoTracking()
            .Where(p => p.UserId == user.Id || p.IsPublic)
            .ToListAsync();

        return SubsonicOk(new Dictionary<string, object>
        {
            ["playlists"] = new Dictionary<string, object>
            {
                ["playlist"] = playlists.Select(PlaylistToDict).ToArray()
            }
        });
    }

    // GET /rest/getPlaylist.view?id=
    [HttpGet("getPlaylist.view")]
    public async Task<IActionResult> GetPlaylist([FromQuery] string id)
    {
        var user = await ResolveSubsonicUser();
        if (user == null) return SubsonicError("Unauthorized", 40);

        if (!long.TryParse(id, out var playlistId)) return SubsonicError("Not found", 70);

        var playlist = await _db.Playlists
            .Include(p => p.Songs!)
                .ThenInclude(ps => ps.Song!)
                    .ThenInclude(s => s.Artist)
            .Include(p => p.Songs!)
                .ThenInclude(ps => ps.Song!)
                    .ThenInclude(s => s.Album)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == playlistId);

        if (playlist == null) return SubsonicError("Playlist not found", 70);
        if (playlist.UserId != user.Id && !playlist.IsPublic)
            return SubsonicError("Not authorized", 50);

        var songs = (playlist.Songs ?? new List<PlaylistSong>())
            .OrderBy(ps => ps.SortOrder)
            .Select(ps => SongToDict(ps.Song!))
            .ToArray();

        var dict = PlaylistToDict(playlist);
        dict["entry"] = songs;

        return SubsonicOk(new Dictionary<string, object> { ["playlist"] = dict });
    }

    // GET /rest/createPlaylist.view
    [HttpGet("createPlaylist.view")]
    public async Task<IActionResult> CreatePlaylist(
        [FromQuery] string? playlistId,
        [FromQuery] string? name,
        [FromQuery(Name = "songId")] List<string>? songIds)
    {
        var user = await ResolveSubsonicUser();
        if (user == null) return SubsonicError("Unauthorized", 40);

        Playlist? playlist = null;

        // Update existing
        if (!string.IsNullOrEmpty(playlistId) && long.TryParse(playlistId, out var pid))
        {
            playlist = await _db.Playlists
                .Include(p => p.Songs)
                .FirstOrDefaultAsync(p => p.Id == pid);
            if (playlist != null && playlist.UserId != user.Id)
                return SubsonicError("Not authorized", 50);
        }

        if (playlist == null)
        {
            playlist = new Playlist
            {
                UserId = user.Id,
                Name = name ?? "New Playlist",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.Playlists.Add(playlist);
            await _db.SaveChangesAsync();
        }
        else if (!string.IsNullOrEmpty(name))
        {
            playlist.Name = name;
        }

        // Add songs
        if (songIds != null && songIds.Count > 0)
        {
            var maxOrder = playlist.Songs?.Max(ps => (int?)ps.SortOrder) ?? 0;
            foreach (var sid in songIds)
            {
                if (long.TryParse(sid, out var songId))
                {
                    if (playlist.Songs?.Any(ps => ps.SongId == songId) == true) continue;
                    if (!await _db.Songs.AnyAsync(s => s.Id == songId)) continue;

                    _db.PlaylistSongs.Add(new PlaylistSong
                    {
                        PlaylistId = playlist.Id,
                        SongId = songId,
                        SortOrder = ++maxOrder
                    });
                }
            }
        }

        playlist.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return SubsonicOk(new Dictionary<string, object> { ["playlist"] = PlaylistToDict(playlist) });
    }

    // GET /rest/updatePlaylist.view
    [HttpGet("updatePlaylist.view")]
    public async Task<IActionResult> UpdatePlaylist(
        [FromQuery] string playlistId,
        [FromQuery] string? name,
        [FromQuery(Name = "songIdToAdd")] List<string>? songIdsToAdd,
        [FromQuery(Name = "songIndexToRemove")] List<int>? songIndexesToRemove)
    {
        var user = await ResolveSubsonicUser();
        if (user == null) return SubsonicError("Unauthorized", 40);

        if (!long.TryParse(playlistId, out var pid))
            return SubsonicError("Not found", 70);

        var playlist = await _db.Playlists
            .Include(p => p.Songs)
            .FirstOrDefaultAsync(p => p.Id == pid);

        if (playlist == null) return SubsonicError("Playlist not found", 70);
        if (playlist.UserId != user.Id) return SubsonicError("Not authorized", 50);

        if (!string.IsNullOrEmpty(name)) playlist.Name = name;

        // Remove songs by index
        if (songIndexesToRemove != null && playlist.Songs != null)
        {
            var sorted = playlist.Songs.OrderBy(ps => ps.SortOrder).ToList();
            foreach (var idx in songIndexesToRemove.OrderByDescending(i => i))
            {
                if (idx >= 0 && idx < sorted.Count)
                {
                    _db.PlaylistSongs.Remove(sorted[idx]);
                }
            }
        }

        // Add songs
        if (songIdsToAdd != null && songIdsToAdd.Count > 0)
        {
            var maxOrder = playlist.Songs?.Max(ps => (int?)ps.SortOrder) ?? 0;
            foreach (var sid in songIdsToAdd)
            {
                if (long.TryParse(sid, out var songId))
                {
                    if (playlist.Songs?.Any(ps => ps.SongId == songId) == true) continue;
                    _db.PlaylistSongs.Add(new PlaylistSong
                    {
                        PlaylistId = playlist.Id,
                        SongId = songId,
                        SortOrder = ++maxOrder
                    });
                }
            }
        }

        playlist.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return SubsonicOk(new Dictionary<string, object>());
    }

    // GET /rest/deletePlaylist.view?id=
    [HttpGet("deletePlaylist.view")]
    public async Task<IActionResult> DeletePlaylist([FromQuery] string id)
    {
        var user = await ResolveSubsonicUser();
        if (user == null) return SubsonicError("Unauthorized", 40);

        if (!long.TryParse(id, out var pid)) return SubsonicError("Not found", 70);

        var playlist = await _db.Playlists.FindAsync(pid);
        if (playlist == null) return SubsonicError("Playlist not found", 70);
        if (playlist.UserId != user.Id) return SubsonicError("Not authorized", 50);

        _db.Playlists.Remove(playlist);
        await _db.SaveChangesAsync();

        return SubsonicOk(new Dictionary<string, object>());
    }
}
