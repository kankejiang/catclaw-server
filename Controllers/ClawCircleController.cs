using CatClawMusicServer.ClawCircle;
using Microsoft.AspNetCore.Mvc;

namespace CatClawMusicServer.Controllers;

/// <summary>
/// 猫爪圈调试 / 状态接口（Bearer 鉴权，与 /api 一致）。
/// 仅用于运维查看当前 tracker 中的在线节点。
/// </summary>
[ApiController]
[Route("api/clawcircle")]
public class ClawCircleController : ControllerBase
{
    private readonly ClawCircleTracker _tracker;

    public ClawCircleController(ClawCircleTracker tracker) => _tracker = tracker;

    /// <summary>获取当前在线节点列表与计数。</summary>
    [HttpGet("peers")]
    public IActionResult Peers()
    {
        var peers = _tracker.Snapshot();
        return Ok(new
        {
            onlineCount = _tracker.OnlineCount,
            peers = peers
        });
    }
}
