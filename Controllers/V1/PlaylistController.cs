using System.IdentityModel.Tokens.Jwt;
using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.V1;

[Authorize]
[ApiController]
[Route("api/v1/playlists")]
public class PlaylistController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public PlaylistController(ApplicationDbContext db) => _db = db;

    // GET /api/v1/playlists
    [HttpGet]
    public async Task<IActionResult> GetPlaylists([FromQuery] int page = 1, [FromQuery] int page_size = 50)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        page = Math.Max(1, page);
        page_size = Math.Clamp(page_size, 1, 200);

        var query = _db.Playlists
            .Where(p => p.UserId == userId || p.IsPublic)
            .AsNoTracking();

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(p => p.UpdatedAt)
            .Skip((page - 1) * page_size)
            .Take(page_size)
            .Select(p => new
            {
                id = p.Id,
                name = p.Name,
                description = p.Description,
                is_public = p.IsPublic,
                song_count = p.Songs != null ? p.Songs.Count : 0,
                created_at = p.CreatedAt,
                updated_at = p.UpdatedAt
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { items, total, page, page_size }));
    }

    // POST /api/v1/playlists
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePlaylistReq req)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));
        if (string.IsNullOrWhiteSpace(req.Name))
            return Ok(ApiResponse<object>.Error(ErrorCodes.InvalidParameter, "名称不能为空"));

        var playlist = new Playlist
        {
            UserId = userId,
            Name = req.Name,
            Description = req.Description,
            IsPublic = req.IsPublic
        };
        _db.Playlists.Add(playlist);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new { id = playlist.Id, name = playlist.Name }));
    }

    // GET /api/v1/playlists/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetPlaylist(long id)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        var playlist = await _db.Playlists
            .Include(p => p.Songs!)
                .ThenInclude(ps => ps.Song!)
                    .ThenInclude(s => s.Artist)
            .Include(p => p.Songs!)
                .ThenInclude(ps => ps.Song!)
                    .ThenInclude(s => s.Album)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (playlist == null)
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "播放列表不存在"));
        if (playlist.UserId != userId && !playlist.IsPublic)
            return Ok(ApiResponse<object>.Error(ErrorCodes.Forbidden, "无权访问"));

        return Ok(ApiResponse<object>.Ok(new
        {
            id = playlist.Id,
            name = playlist.Name,
            description = playlist.Description,
            is_public = playlist.IsPublic,
            created_at = playlist.CreatedAt,
            updated_at = playlist.UpdatedAt,
            songs = playlist.Songs?
                .OrderBy(ps => ps.SortOrder)
                .Select(ps => new
                {
                    id = ps.Song!.Id,
                    title = ps.Song.Title,
                    artist = ps.Song.Artist?.Name ?? "未知艺术家",
                    album = ps.Song.Album?.Title ?? "未知专辑",
                    album_cover = ps.Song.Album?.Cover ?? ps.Song.CoverArtPath,
                    duration = ps.Song.Duration,
                    sort_order = ps.SortOrder,
                    added_at = ps.AddedAt
                }).ToList()
        }));
    }

    // PUT /api/v1/playlists/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdatePlaylistReq req)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        var playlist = await _db.Playlists.FindAsync(id);
        if (playlist == null)
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "播放列表不存在"));
        if (playlist.UserId != userId)
            return Ok(ApiResponse<object>.Error(ErrorCodes.Forbidden, "无权修改"));

        if (req.Name != null) playlist.Name = req.Name;
        if (req.Description != null) playlist.Description = req.Description;
        playlist.IsPublic = req.IsPublic ?? playlist.IsPublic;
        playlist.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(null!));
    }

    // DELETE /api/v1/playlists/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(long id)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        var playlist = await _db.Playlists.FindAsync(id);
        if (playlist == null)
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "播放列表不存在"));
        if (playlist.UserId != userId)
            return Ok(ApiResponse<object>.Error(ErrorCodes.Forbidden, "无权删除"));

        _db.Playlists.Remove(playlist);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(null!));
    }

    // POST /api/v1/playlists/{id}/songs
    [HttpPost("{id}/songs")]
    public async Task<IActionResult> AddSongs(long id, [FromBody] AddSongsReq req)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        var playlist = await _db.Playlists.FindAsync(id);
        if (playlist == null)
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "播放列表不存在"));
        if (playlist.UserId != userId)
            return Ok(ApiResponse<object>.Error(ErrorCodes.Forbidden, "无权修改"));

        if (req.SongIds == null || req.SongIds.Length == 0)
            return Ok(ApiResponse<object>.Error(ErrorCodes.InvalidParameter, "歌曲列表不能为空"));

        var maxOrder = await _db.PlaylistSongs
            .Where(ps => ps.PlaylistId == id)
            .MaxAsync(ps => (int?)ps.SortOrder) ?? 0;

        var position = req.Position ?? maxOrder + 1;

        foreach (var songId in req.SongIds)
        {
            if (await _db.PlaylistSongs.AnyAsync(ps => ps.PlaylistId == id && ps.SongId == songId))
                continue;
            if (!await _db.Songs.AnyAsync(s => s.Id == songId))
                continue;

            _db.PlaylistSongs.Add(new PlaylistSong
            {
                PlaylistId = id,
                SongId = songId,
                SortOrder = position++
            });
        }

        playlist.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(null!));
    }

    // DELETE /api/v1/playlists/{id}/songs/{songId}
    [HttpDelete("{id}/songs/{songId}")]
    public async Task<IActionResult> RemoveSong(long id, long songId)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        var playlist = await _db.Playlists.FindAsync(id);
        if (playlist == null)
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "播放列表不存在"));
        if (playlist.UserId != userId)
            return Ok(ApiResponse<object>.Error(ErrorCodes.Forbidden, "无权修改"));

        var ps = await _db.PlaylistSongs.FirstOrDefaultAsync(ps => ps.PlaylistId == id && ps.SongId == songId);
        if (ps != null)
        {
            _db.PlaylistSongs.Remove(ps);
            playlist.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return Ok(ApiResponse<object>.Ok(null!));
    }

    // PUT /api/v1/playlists/{id}/reorder
    [HttpPut("{id}/reorder")]
    public async Task<IActionResult> Reorder(long id, [FromBody] ReorderReq req)
    {
        var userId = GetUserId();
        if (userId == 0) return Ok(ApiResponse<object>.Error(ErrorCodes.Unauthorized, "未登录"));

        var playlist = await _db.Playlists.FindAsync(id);
        if (playlist == null)
            return Ok(ApiResponse<object>.Error(ErrorCodes.NotFound, "播放列表不存在"));
        if (playlist.UserId != userId)
            return Ok(ApiResponse<object>.Error(ErrorCodes.Forbidden, "无权修改"));

        if (req.SongIds == null || req.SongIds.Length == 0)
            return Ok(ApiResponse<object>.Error(ErrorCodes.InvalidParameter, "歌曲列表不能为空"));

        var existing = await _db.PlaylistSongs
            .Where(ps => ps.PlaylistId == id)
            .ToListAsync();

        for (int i = 0; i < req.SongIds.Length; i++)
        {
            var ps = existing.FirstOrDefault(ps => ps.SongId == req.SongIds[i]);
            if (ps != null)
                ps.SortOrder = i + 1;
        }

        playlist.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(null!));
    }

    private long GetUserId()
    {
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return long.TryParse(sub, out var id) ? id : 0;
    }
}

// ── 请求 DTO ──
public record CreatePlaylistReq(string Name, string? Description, bool IsPublic = false);
public record UpdatePlaylistReq(string? Name, string? Description, bool? IsPublic);
public record AddSongsReq(long[] SongIds, int? Position);
public record ReorderReq(long[] SongIds);
