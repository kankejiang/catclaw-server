using CatClawMusicServer.ClawCircle;
using CatClawMusicServer.ClawCircle.Accounts;
using CatClawMusicServer.ClawCircle.Dht;
using CatClawMusicServer.ClawCircle.Ledger;
using CatClawMusicServer.ClawCircle.Transfer;
using CatClawMusicServer.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatClawMusicServer.Controllers;

[ApiController]
[Route("api/clawcircle")]
public class ClawCircleController : ControllerBase
{
    private readonly ClawCircleTracker _tracker;
    private readonly DhtService _dht;
    private readonly DhtOptions _dhtOpts;
    private readonly TransferEngine _transfer;
    private readonly UdpTransferProtocol _udp;
    private readonly NodeReputation _reputation;
    private readonly BlockchainLedger _ledger;
    private readonly AccountService _accounts;
    private readonly IDbContextFactory<ApplicationDbContext> _dbf;

    public ClawCircleController(
        ClawCircleTracker tracker,
        DhtService dht,
        DhtOptions dhtOpts,
        TransferEngine transfer,
        UdpTransferProtocol udp,
        NodeReputation reputation,
        BlockchainLedger ledger,
        AccountService accounts,
        IDbContextFactory<ApplicationDbContext> dbf)
    {
        _tracker = tracker;
        _dht = dht;
        _dhtOpts = dhtOpts;
        _transfer = transfer;
        _udp = udp;
        _reputation = reputation;
        _ledger = ledger;
        _accounts = accounts;
        _dbf = dbf;
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
            },
            ledger = new
            {
                height = _ledger.Height,
                totalNodes = _ledger.AllBalances().Count,
                totalSupply = _ledger.AllBalances().Values.Sum(),
                sizeBytes = _ledger.EstimatedSizeBytes,
                prunedToIndex = _ledger.PrunedToIndex,
                rules = new
                {
                    currency = "小鱼干 🐟",
                    initialBalance = BlockchainLedger.InitialBalance,
                    fishPerHourOnline = BlockchainLedger.FishPerHour,
                    fishPerGB = BlockchainLedger.FishPerGB
                }
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

    // ── 区块链积分账本 ──

    [HttpGet("ledger/balance")]
    public IActionResult GetBalance([FromQuery] string deviceId, [FromQuery] long? accountId)
    {
        long balance;
        List<Transaction> history;
        if (accountId.HasValue)
        {
            balance = _ledger.GetBalance(accountId.Value);
            history = _ledger.GetHistory(accountId.Value);
        }
        else if (!string.IsNullOrEmpty(deviceId))
        {
            balance = _ledger.GetBalanceByDeviceAsync(deviceId).GetAwaiter().GetResult();
            history = _ledger.GetHistoryByDevice(deviceId);
        }
        else
        {
            return BadRequest("需要 deviceId 或 accountId 参数");
        }
        return Ok(new
        {
            accountId,
            deviceId,
            balance,
            history = history.Take(50)
        });
    }

    [HttpGet("ledger/balances")]
    public IActionResult GetAllBalances()
    {
        var balances = _ledger.AllBalances()
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new { deviceId = kv.Key, balance = kv.Value })
            .ToList();
        return Ok(new { totalNodes = balances.Count, balances });
    }

    [HttpGet("ledger/chain")]
    public IActionResult GetChain([FromQuery] int from = 0, [FromQuery] int count = 20)
    {
        var chain = _ledger.GetChain();
        var slice = chain.Skip(from).Take(count).ToList();
        return Ok(new
        {
            height = _ledger.Height,
            valid = _ledger.ValidateChain(),
            sizeBytes = _ledger.EstimatedSizeBytes,
            prunedToIndex = _ledger.PrunedToIndex,
            blocks = slice
        });
    }

    [HttpGet("ledger/history/{deviceIdOrAccountId}")]
    public IActionResult GetHistory(string deviceIdOrAccountId)
    {
        // 优先按 accountId 解析（数字），否则按 deviceId
        List<Transaction> history;
        long balance;
        if (long.TryParse(deviceIdOrAccountId, out var accId))
        {
            history = _ledger.GetHistory(accId);
            balance = _ledger.GetBalance(accId);
        }
        else
        {
            history = _ledger.GetHistoryByDevice(deviceIdOrAccountId);
            balance = _ledger.GetBalanceByDeviceAsync(deviceIdOrAccountId).GetAwaiter().GetResult();
        }
        return Ok(new
        {
            query = deviceIdOrAccountId,
            balance,
            totalTransactions = history.Count,
            transactions = history.Take(100)
        });
    }

    [HttpGet("transfers")]
    public IActionResult Transfers()
    {
        var list = _udp.GetAllTransfers();
        return Ok(new
        {
            activeCount = list.Count,
            transfers = list
        });
    }

    [HttpGet("transfers/{taskId}")]
    public IActionResult GetTransfer(string taskId)
    {
        var info = _udp.GetTransfer(taskId);
        if (info == null) return NotFound(new { error = "task not found" });
        return Ok(info);
    }

    [HttpPost("transfers/{taskId}/cancel")]
    public async Task<IActionResult> CancelTransfer(string taskId)
    {
        var ok = await _udp.CancelTransferAsync(taskId);
        return Ok(new { cancelled = ok, taskId });
    }

    /// <summary>本节点作为发送方：为指定歌曲生成分块清单并注册发送上下文。</summary>
    [HttpPost("transfers/offer")]
    public async Task<IActionResult> OfferTransfer([FromBody] OfferTransferRequest req)
    {
        if (string.IsNullOrEmpty(req.SongId) || string.IsNullOrEmpty(req.PeerDeviceId))
            return BadRequest("需要 songId 和 peerDeviceId");

        var peer = _tracker.Find(req.PeerDeviceId);
        if (peer == null) return NotFound(new { error = "peer not online" });
        if (_reputation.IsBlacklisted(req.PeerDeviceId))
            return BadRequest(new { error = "对端节点信誉过低，已被列入黑名单" });
        if (string.IsNullOrEmpty(peer.Wan) || peer.Port == null)
            return BadRequest(new { error = "peer 未完成 STUN 反射端点探测" });

        // 从数据库查歌曲文件路径
        await using var db = await _dbf.CreateDbContextAsync();
        var song = await db.Songs.FindAsync(long.TryParse(req.SongId, out var sid) ? sid : 0L);
        if (song == null) return NotFound(new { error = "song not found" });
        if (string.IsNullOrEmpty(song.FilePath) || !System.IO.File.Exists(song.FilePath))
            return NotFound(new { error = "song file not found on disk" });

        var manifest = await _transfer.CreateManifestAsync(song.FilePath);
        var taskId = req.TaskId ?? Guid.NewGuid().ToString("N")[..12];
        var peerEp = new System.Net.IPEndPoint(System.Net.IPAddress.Parse(peer.Wan!), peer.Port.Value);
        _udp.RegisterSender(taskId, song.FilePath, req.PeerDeviceId, manifest, peerEp);

        return Ok(new
        {
            taskId,
            manifest,
            peerEndpoint = new { ip = peer.Wan, port = peer.Port },
            message = "已注册发送方，等待对端 ChunkRequest"
        });
    }

    /// <summary>本节点作为接收方：根据对端发来的清单创建接收任务。</summary>
    [HttpPost("transfers/receive")]
    public async Task<IActionResult> ReceiveTransfer([FromBody] ReceiveTransferRequest req)
    {
        if (req.Manifest == null || string.IsNullOrEmpty(req.PeerDeviceId) || string.IsNullOrEmpty(req.TaskId))
            return BadRequest("需要 manifest、peerDeviceId、taskId");

        var peer = _tracker.Find(req.PeerDeviceId);
        if (peer == null) return NotFound(new { error = "peer not online" });
        if (_reputation.IsBlacklisted(req.PeerDeviceId))
            return BadRequest(new { error = "对端节点信誉过低，已被列入黑名单" });
        if (string.IsNullOrEmpty(peer.Wan) || peer.Port == null)
            return BadRequest(new { error = "peer 未完成 STUN 反射端点探测" });

        var outputDir = System.IO.Path.Combine("Data", "p2p_downloads");
        var task = _transfer.CreateReceiveTask(req.Manifest, outputDir);
        var peerEp = new System.Net.IPEndPoint(System.Net.IPAddress.Parse(peer.Wan!), peer.Port.Value);
        _udp.RegisterReceiver(req.TaskId, task.Id, req.PeerDeviceId, peerEp);

        return Ok(new
        {
            taskId = req.TaskId,
            engineTaskId = task.Id,
            outputFile = task.OutputFile,
            peerEndpoint = new { ip = peer.Wan, port = peer.Port }
        });
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

    // ── 账号端点 ──

    /// <summary>注册猫爪驿站账号。</summary>
    [HttpPost("account/register")]
    public async Task<IActionResult> RegisterAccount([FromBody] RegisterAccountRequest req)
    {
        if (string.IsNullOrEmpty(req.Username) || string.IsNullOrEmpty(req.Password))
            return BadRequest("用户名和密码不能为空");
        var (account, error) = await _accounts.RegisterAsync(req.Username, req.Password, req.DisplayName ?? "");
        if (account == null) return BadRequest(error);
        // 账本注册（赠送初始小鱼干）
        _ledger.RegisterAccount(account.Id);
        return Ok(new
        {
            accountId = account.Id,
            username = account.Username,
            displayName = account.DisplayName,
            balance = _ledger.GetBalance(account.Id)
        });
    }

    /// <summary>登录并签发设备 Token。Token 仅此一次返回，请妥善保存。</summary>
    [HttpPost("account/login")]
    public async Task<IActionResult> Login([FromBody] ClawLoginRequest req)
    {
        if (string.IsNullOrEmpty(req.Username) || string.IsNullOrEmpty(req.Password) || string.IsNullOrEmpty(req.DeviceId))
            return BadRequest("用户名、密码、deviceId 不能为空");
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var (result, error) = await _accounts.LoginAsync(req.Username, req.Password, req.DeviceId, req.DeviceName ?? "", ip);
        if (result == null) return BadRequest(error);
        // 绑定设备到账本账号
        _ledger.BindDeviceToAccount(req.DeviceId, result.AccountId);
        return Ok(new
        {
            token = result.Token,
            accountId = result.AccountId,
            username = result.Username,
            displayName = result.DisplayName,
            deviceId = result.DeviceId,
            deviceName = result.DeviceName,
            balance = _ledger.GetBalance(result.AccountId)
        });
    }

    /// <summary>查询当前账号信息（通过 Token）。</summary>
    [HttpGet("account/me")]
    public async Task<IActionResult> GetMe([FromQuery] string token)
    {
        var account = await _accounts.ValidateTokenAsync(token);
        if (account == null) return Unauthorized("无效 Token");
        return Ok(new
        {
            accountId = account.Id,
            username = account.Username,
            displayName = account.DisplayName,
            balance = _ledger.GetBalance(account.Id),
            createdAt = account.CreatedAt,
            lastLoginAt = account.LastLoginAt
        });
    }

    /// <summary>列出账号下所有设备。</summary>
    [HttpGet("account/devices")]
    public async Task<IActionResult> ListDevices([FromQuery] string token)
    {
        var account = await _accounts.ValidateTokenAsync(token);
        if (account == null) return Unauthorized("无效 Token");
        var devices = await _accounts.ListDevicesAsync(account.Id);
        return Ok(new { devices });
    }

    /// <summary>吊销指定设备 Token（退出登录）。</summary>
    [HttpDelete("account/devices/{deviceId}")]
    public async Task<IActionResult> RevokeDevice(string deviceId, [FromQuery] string token)
    {
        var account = await _accounts.ValidateTokenAsync(token);
        if (account == null) return Unauthorized("无效 Token");
        var ok = await _accounts.RevokeDeviceAsync(account.Id, deviceId);
        return ok ? Ok(new { message = "设备已退出" }) : NotFound("设备不存在");
    }

    /// <summary>修改密码（所有设备 Token 自动失效，需重新登录）。</summary>
    [HttpPost("account/change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        if (string.IsNullOrEmpty(req.Token) || string.IsNullOrEmpty(req.OldPassword) || string.IsNullOrEmpty(req.NewPassword))
            return BadRequest("参数不完整");
        var account = await _accounts.ValidateTokenAsync(req.Token);
        if (account == null) return Unauthorized("无效 Token");
        var (ok, error) = await _accounts.ChangePasswordAsync(account.Id, req.OldPassword, req.NewPassword);
        return ok ? Ok(new { message = "密码已修改，所有设备需重新登录" }) : BadRequest(error);
    }
}

public record ToggleDhtRequest(bool Enabled);
public record BootstrapDhtRequest(string Address);
public record RegisterAccountRequest(string Username, string Password, string? DisplayName);
public record ClawLoginRequest(string Username, string Password, string DeviceId, string? DeviceName);
public record ChangePasswordRequest(string Token, string OldPassword, string NewPassword);
public record OfferTransferRequest(string SongId, string PeerDeviceId, string? TaskId);
public record ReceiveTransferRequest(string TaskId, string PeerDeviceId, PieceManifest Manifest);

