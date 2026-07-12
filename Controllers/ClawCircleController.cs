using CatClawMusicServer.ClawCircle;
using CatClawMusicServer.ClawCircle.Dht;
using CatClawMusicServer.ClawCircle.Transfer;
using Microsoft.AspNetCore.Mvc;

namespace CatClawMusicServer.Controllers;

[ApiController]
[Route("api/clawcircle")]
public class ClawCircleController : ControllerBase
{
    private readonly ClawCircleTracker _tracker;
    private readonly DhtService _dht;
    private readonly DhtOptions _dhtOpts;
    private readonly TransferEngine _transfer;
    private readonly NodeReputation _reputation;

    public ClawCircleController(
        ClawCircleTracker tracker,
        DhtService dht,
        DhtOptions dhtOpts,
        TransferEngine transfer,
        NodeReputation reputation)
    {
        _tracker = tracker;
        _dht = dht;
        _dhtOpts = dhtOpts;
        _transfer = transfer;
        _reputation = reputation;
    }

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

    [HttpGet("stats")]
    public IActionResult Stats()
    {
        return Ok(new
        {
            tracker = new
            {
                onlinePeers = _tracker.OnlineCount
            },
            dht = new
            {
                enabled = _dhtOpts.Enabled,
                localId = _dht.LocalId.ToString(),
                nodeCount = _dht.NodeCount,
                storeCount = _dht.StoreCount
            },
            reputation = new
            {
                trackedNodes = _reputation.All().Count,
                blacklisted = _reputation.All().Count(kv => kv.Value.Score < NodeReputation.BlacklistThreshold),
                trusted = _reputation.All().Count(kv => kv.Value.Score >= NodeReputation.TrustedThreshold)
            }
        });
    }

    [HttpGet("dht/nodes")]
    public IActionResult DhtNodes()
    {
        if (!_dhtOpts.Enabled)
            return Ok(new { enabled = false, nodes = Array.Empty<object>() });

        var nodes = _dht.GetAllNodes().Select(n => new
        {
            id = n.Id.ToString(),
            address = n.Endpoint.ToString(),
            lastSeen = n.LastSeen,
            reputation = _reputation.GetReputation(n.Id.ToString()),
            isAlive = n.IsAlive
        }).ToList();

        return Ok(new { enabled = true, nodes });
    }

    [HttpGet("reputation")]
    public IActionResult Reputation()
    {
        var records = _reputation.All().Select(kv => new
        {
            nodeId = kv.Key,
            score = kv.Value.Score,
            successCount = kv.Value.SuccessCount,
            failureCount = kv.Value.FailureCount,
            successRate = kv.Value.SuccessRate,
            lastSeen = kv.Value.LastSeen,
            isBlacklisted = kv.Value.Score < NodeReputation.BlacklistThreshold,
            isTrusted = kv.Value.Score >= NodeReputation.TrustedThreshold
        }).ToList();

        return Ok(records);
    }

    [HttpGet("transfers")]
    public IActionResult Transfers()
    {
        return Ok(new { message = "Transfer engine active" });
    }

    [HttpGet("library/status")]
    public IActionResult LibraryStatus()
    {
        return Ok(new
        {
            songCount = _dht.LibrarySongCount,
            lastPublish = _dht.LastLibraryPublish,
            dhtStoreCount = _dht.StoreCount
        });
    }

    [HttpGet("find-song")]
    public async Task<IActionResult> FindSong([FromQuery] string artist, [FromQuery] string title)
    {
        if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(title))
            return BadRequest("需要 artist 和 title 参数");

        var songKey = (artist + "\x01" + title).ToLowerInvariant();
        var holders = await _dht.FindSongHoldersAsync(songKey);

        return Ok(new
        {
            query = $"{artist} - {title}",
            holders = holders.Select(h => new
            {
                nodeId = h.NodeId[..Math.Min(16, h.NodeId.Length)] + "...",
                address = h.Address,
                songCount = h.SongCount,
                timestamp = h.Timestamp,
                isExact = h.IsExact
            }).ToList(),
            totalHolders = holders.Count
        });
    }

    [HttpPost("dht/toggle")]
    public async Task<IActionResult> ToggleDht([FromBody] ToggleDhtRequest req)
    {
        _dhtOpts.Enabled = req.Enabled;

        if (req.Enabled)
        {
            try { _dht.Start(); }
            catch { /* 可能已启动 */ }

            // 自动从默认种子节点 Bootstrap
            foreach (var node in _dhtOpts.BootstrapNodes)
            {
                if (!string.IsNullOrEmpty(node))
                    _ = _dht.BootstrapFromAddressAsync(node);
            }
        }
        else
        {
            try { _dht.Stop(); }
            catch { /* 可能已停止 */ }
        }

        return Ok(new { enabled = _dhtOpts.Enabled });
    }

    [HttpPost("dht/bootstrap")]
    public async Task<IActionResult> BootstrapDht([FromBody] BootstrapDhtRequest req)
    {
        if (!_dhtOpts.Enabled)
            return BadRequest("DHT 未启用");

        if (string.IsNullOrEmpty(req.Address))
            return BadRequest("地址不能为空");

        var ok = await _dht.BootstrapFromAddressAsync(req.Address);
        return ok
            ? Ok(new { message = "Bootstrap 成功" })
            : BadRequest("Bootstrap 失败，请检查地址格式（示例: 192.168.1.100:37825 或 domain.com:37825）");
    }
}

public record ToggleDhtRequest(bool Enabled);
public record BootstrapDhtRequest(string Address);

