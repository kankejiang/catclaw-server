using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.V1;

[Authorize]
[ApiController]
[Route("api/v1/library")]
public class LibraryController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public LibraryController(ApplicationDbContext db) => _db = db;

    [HttpGet("overview")]
    public async Task<IActionResult> Overview()
    {
        var songCount = await _db.Songs.CountAsync();
        var artistCount = await _db.Artists.CountAsync();
        var albumCount = await _db.Albums.CountAsync();
        var playlistCount = await _db.Playlists.CountAsync();

        var recentSongs = await _db.Songs
            .OrderByDescending(s => s.DateAdded)
            .Take(10)
            .Select(s => new { s.Id, s.Title, s.DateAdded })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            song_count = songCount,
            artist_count = artistCount,
            album_count = albumCount,
            playlist_count = playlistCount,
            recent_songs = recentSongs
        }));
    }
}
