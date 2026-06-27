using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

namespace CatClawMusicServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlaylistsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public PlaylistsController(ApplicationDbContext db)
    {
        _db = db;
    }

    // GET /api/playlists?page=1&page_size=50
    [HttpGet]
    public async Task<IActionResult> GetPlaylists(
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 50)
    {
        if (page < 1) page = 1;
        if (page_size < 1) page_size = 50;

        var total = await _db.Playlists.CountAsync();

        var playlists = await _db.Playlists
            .AsNoTracking()
            .OrderByDescending(p => p.UpdatedAt)
            .Skip((page - 1) * page_size)
            .Take(page_size)
            .Select(p => new
            {
                id = p.Id,
                name = p.Name,
                description = p.Description,
                created_at = p.CreatedAt,
                updated_at = p.UpdatedAt,
                song_count = _db.PlaylistSongs.Count(ps => ps.PlaylistId == p.Id)
            })
            .ToListAsync();

        return Ok(new { playlists, total });
    }

    // GET /api/playlists/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetPlaylist(long id)
    {
        var playlist = await _db.Playlists
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (playlist == null) return NotFound();

        var songs = await _db.PlaylistSongs
            .Include(ps => ps.Song)
            .ThenInclude(s => s!.Artist)
            .Include(ps => ps.Song)
            .ThenInclude(s => s!.Album)
            .AsNoTracking()
            .Where(ps => ps.PlaylistId == id)
            .OrderBy(ps => ps.SortOrder)
            .Select(ps => new
            {
                id = ps.Song!.Id,
                title = ps.Song.Title,
                artist = ps.Song.Artist != null ? ps.Song.Artist.Name : "未知艺术家",
                album = ps.Song.Album != null ? ps.Song.Album.Title : "未知专辑",
                duration = ps.Song.Duration,
                sort_order = ps.SortOrder
            })
            .ToListAsync();

        return Ok(new
        {
            id = playlist.Id,
            name = playlist.Name,
            description = playlist.Description,
            created_at = playlist.CreatedAt,
            updated_at = playlist.UpdatedAt,
            songs
        });
    }

    // POST /api/playlists
    [HttpPost]
    public async Task<IActionResult> CreatePlaylist([FromBody] CreatePlaylistRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Name))
            return BadRequest("name required");

        var playlist = new Playlist
        {
            Name = body.Name,
            Description = body.Description
        };

        _db.Playlists.Add(playlist);
        await _db.SaveChangesAsync();

        return Created($"/api/playlists/{playlist.Id}", new { id = playlist.Id });
    }

    // PUT /api/playlists/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePlaylist(long id, [FromBody] UpdatePlaylistRequest req)
    {
        var playlist = await _db.Playlists.FindAsync(id);
        if (playlist == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.Name))
            playlist.Name = req.Name;
        if (req.Description != null)
            playlist.Description = req.Description;
        playlist.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // DELETE /api/playlists/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePlaylist(long id)
    {
        var playlist = await _db.Playlists.FindAsync(id);
        if (playlist == null) return NotFound();

        _db.Playlists.Remove(playlist);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // POST /api/playlists/{id}/songs
    [HttpPost("{id}/songs")]
    public async Task<IActionResult> AddSongToPlaylist(long id, [FromBody] AddSongRequest req)
    {
        var exists = await _db.Playlists.AnyAsync(p => p.Id == id);
        if (!exists) return NotFound();

        var songExists = await _db.Songs.AnyAsync(s => s.Id == req.SongId);
        if (!songExists) return BadRequest("song not found");

        var maxOrder = await _db.PlaylistSongs
            .Where(ps => ps.PlaylistId == id)
            .MaxAsync(ps => (int?)ps.SortOrder) ?? 0;

        var entry = new PlaylistSong
        {
            PlaylistId = id,
            SongId = req.SongId,
            SortOrder = maxOrder + 1
        };

        _db.PlaylistSongs.Add(entry);
        await _db.SaveChangesAsync();

        return Ok(new { id = entry.Id });
    }

    // DELETE /api/playlists/{id}/songs/{songId}
    [HttpDelete("{id}/songs/{songId}")]
    public async Task<IActionResult> RemoveSongFromPlaylist(long id, long songId)
    {
        var entry = await _db.PlaylistSongs
            .FirstOrDefaultAsync(ps => ps.PlaylistId == id && ps.SongId == songId);

        if (entry == null) return NotFound();

        _db.PlaylistSongs.Remove(entry);

        // 重新排序
        var remaining = await _db.PlaylistSongs
            .Where(ps => ps.PlaylistId == id)
            .OrderBy(ps => ps.SortOrder)
            .ToListAsync();

        for (int i = 0; i < remaining.Count; i++)
            remaining[i].SortOrder = i + 1;

        await _db.SaveChangesAsync();
        return NoContent();
    }
}

// DTOs
public class CreatePlaylistRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
public class UpdatePlaylistRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
public class AddSongRequest
{
    [JsonPropertyName("songId")]
    public long SongId { get; set; }
}
