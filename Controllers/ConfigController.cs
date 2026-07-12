using CatClawMusicServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace CatClawMusicServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigController : ControllerBase
{
    private readonly ScannerOptions _options;

    public ConfigController(ScannerOptions options)
    {
        _options = options;
    }

    // GET /api/config
    [HttpGet]
    public IActionResult GetConfig()
    {
        return Ok(new
        {
            musicDirectory = _options.MusicDirectory
        });
    }
}
