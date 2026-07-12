using CatClawMusicServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers.Subsonic;

public partial class SubsonicController
{
    // GET /rest/ping.view
    [HttpGet("ping.view")]
    public IActionResult Ping() => SubsonicOk(new Dictionary<string, object>());

    // GET /rest/getLicense.view
    [HttpGet("getLicense.view")]
    public IActionResult GetLicense()
    {
        return SubsonicOk(new Dictionary<string, object>
        {
            ["license"] = new Dictionary<string, object>
            {
                ["valid"] = true,
                ["email"] = "catclaw@local",
                ["licenseExpires"] = DateTime.UtcNow.AddYears(10).ToString("o")
            }
        });
    }

    // GET /rest/getUser.view
    [HttpGet("getUser.view")]
    public async Task<IActionResult> GetUser([FromQuery] string? username)
    {
        var user = await ResolveSubsonicUser();
        if (user == null) return SubsonicError("Unauthorized", 40);

        return SubsonicOk(new Dictionary<string, object>
        {
            ["user"] = new Dictionary<string, object>
            {
                ["username"] = user.Username,
                ["email"] = "",
                ["scrobblingEnabled"] = true,
                ["adminRole"] = user.Role == "admin",
                ["settingsRole"] = true,
                ["downloadRole"] = true,
                ["uploadRole"] = false,
                ["playlistRole"] = true,
                ["coverArtRole"] = true,
                ["commentRole"] = false,
                ["podcastRole"] = false,
                ["streamRole"] = true,
                ["jukeboxRole"] = false,
                ["shareRole"] = false,
                ["folder"] = new[] { 1 }
            }
        });
    }

    // GET /rest/getScanStatus.view
    [HttpGet("getScanStatus.view")]
    public IActionResult GetScanStatus()
    {
        // TODO: integrate with actual scan service
        return SubsonicOk(new Dictionary<string, object>
        {
            ["scanStatus"] = new Dictionary<string, object>
            {
                ["scanning"] = false,
                ["count"] = 0
            }
        });
    }
}
