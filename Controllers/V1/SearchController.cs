using CatClawMusicServer.Data;
using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.V1;

[Authorize]
[ApiController]
[Route("api/v1/search")]
public class SearchController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public SearchController(ApplicationDbContext db) => _db = db;

    // GET /api/v1/search?q=keyword
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? q = null,
        [FromQuery] int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(ApiResponse<object>.Ok(new
            {
                songs = Array.Empty<object>(),
                artists = Array.Empty<object>(),
                albums = Array.Empty<object>()
            }));

        limit = Math.Clamp(limit, 1, 50);
        var keyword = q.Trim();

        // 并发搜索三个维度（参考 Navidrome 模式）
        var songsTask = _db.Songs
            .Include(s => s.Artist)
            .Include(s => s.Album)
            .AsNoTracking()
            .Where(s => s.Title.Contains(keyword)
                || (s.Artist != null && s.Artist.Name.Contains(keyword))
                || (s.Album != null && s.Album.Title.Contains(keyword)))
            .Take(limit)
            .Select(s => new
            {
                id = s.Id,
                title = s.Title,
                artist = s.Artist != null ? s.Artist.Name : "未知艺术家",
                album = s.Album != null ? s.Album.Title : "未知专辑",
                album_cover = s.Album != null ? s.Album.Cover : s.CoverArtPath,
                duration = s.Duration
            })
            .ToListAsync();

        var artistsTask = _db.Artists
            .AsNoTracking()
            .Where(a => a.Name.Contains(keyword))
            .Take(limit)
            .Select(a => new
            {
                id = a.Id,
                name = a.Name,
                cover = a.Cover
            })
            .ToListAsync();

        var albumsTask = _db.Albums
            .Include(a => a.Artist)
            .AsNoTracking()
            .Where(a => a.Title.Contains(keyword)
                || (a.Artist != null && a.Artist.Name.Contains(keyword)))
            .Take(limit)
            .Select(a => new
            {
                id = a.Id,
                title = a.Title,
                artist = a.Artist != null ? a.Artist.Name : "未知艺术家",
                cover = a.Cover
            })
            .ToListAsync();

        await Task.WhenAll(songsTask, artistsTask, albumsTask);

        return Ok(ApiResponse<object>.Ok(new
        {
            songs = await songsTask,
            artists = await artistsTask,
            albums = await albumsTask
        }));
    }
}
