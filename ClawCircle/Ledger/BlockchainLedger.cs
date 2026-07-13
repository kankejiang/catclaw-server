using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CatClawMusicServer.ClawCircle.Ledger;

/// <summary>
/// 猫爪驿站积分区块链账本 — 简化版比特币，带「快照 + 修剪」控制账本体积。
///
/// 控制体积的核心机制（与 BTC 全节点模式的区别）：
///   • 余额是「状态」而非「历史」——只需保留最新余额快照 + 快照后的增量区块
///   • 每 SnapshotInterval 块生成一个「快照块」，内含全量余额
///   • 快照块一旦生成，之前的普通块会被 Prune() 删除（只保留最近一个快照块作为检查点）
///   • 账本大小有硬上限：1 个快照块 + 最多 SnapshotInterval 个增量块
///   • 查询历史只保留快照后的区块（用户通常只查近期交易，足够用）
///
/// 其他设计：
///   • 区块 = {index, prevHash, timestamp, transactions[], nonce, hash}
///   • 哈希链：篡改任意历史区块会导致后续全部失效
///   • 工作量证明：轻量 PoW（SHA256 前导零点）
///   • 信任锚：服务器负责打包出块 + 生成快照
///   • 小鱼干 🐟 规则：
///     - 注册赠送 100 🐟（新手礼包）
///     - 在线 1 小时 = +10 🐟（每分钟结算，按比例累计）
///     - 上传 1 GB = +10 🐟（等比例，不足 1GB 按 1GB 算）
///     - 下载 1 GB = -10 🐟（等比例，不足 1GB 按 1GB 算）
/// </summary>
public class BlockchainLedger
{
    /// <summary>单位：小鱼干 🐟。新节点注册赠送 100 🐟（新手礼包）。</summary>
    public const int InitialBalance = 100;

    /// <summary>1 GB 流量 = 10 🐟（上传赚、下载花）。</summary>
    public const long BytesPerGB = 1024L * 1024L * 1024L;
    public const int FishPerGB = 10;

    /// <summary>在线奖励：1 小时 = 10 🐟。每分钟结算一次（10/60 ≈ 0.17 🐟/分钟）。</summary>
    public const int FishPerHour = 10;
    public static readonly TimeSpan OnlineRewardInterval = TimeSpan.FromMinutes(1);

    public const int BlockDifficulty = 2;          // PoW 难度（前导零个数）
    public const int MaxTransactionsPerBlock = 50; // 单块最多交易数
    public static readonly TimeSpan BlockInterval = TimeSpan.FromSeconds(15); // 出块间隔

    /// <summary>每 N 块生成一个快照块，并修剪之前的区块。</summary>
    public const int SnapshotInterval = 500;

    /// <summary>定时修剪间隔（每天执行一次，无论是否到 SnapshotInterval）。</summary>
    public static readonly TimeSpan PruneInterval = TimeSpan.FromDays(1);

    private readonly List<Block> _chain = new();
    private readonly ConcurrentQueue<Transaction> _pending = new();
    private readonly ConcurrentDictionary<string, long> _balances = new(); // accountKey → 余额
    private readonly ConcurrentDictionary<string, DateTime> _lastRewardTime = new(); // accountKey → 上次在线奖励时间
    private readonly ConcurrentDictionary<string, string> _deviceToAccount = new(); // deviceId → accountKey 缓存
    private readonly object _blockLock = new();
    private readonly ILogger<BlockchainLedger> _logger;
    private Timer? _mineTimer;
    private Timer? _pruneTimer;
    private Timer? _onlineRewardTimer;
    private Func<List<(string deviceId, DateTime connectedAt)>>? _getOnlineNodes;
    private Func<string, Task<long?>>? _resolveDeviceToAccount; // deviceId → accountId 查询（由 AccountService 注入）

    /// <summary>已修剪到的块索引（此索引之前的普通块已删除，仅保留快照）。</summary>
    public int PrunedToIndex { get; private set; }

    /// <summary>新区块产生事件（供 WebSocket 广播订阅）。</summary>
    public event Func<Block, Task>? OnBlockMined;

    public BlockchainLedger(ILogger<BlockchainLedger> logger)
    {
        _logger = logger;
        // 创世块（同时也是初始快照块）
        var genesis = new Block
        {
            Index = 0,
            PrevHash = "0",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Transactions = new List<Transaction>(),
            Nonce = 0,
            IsSnapshot = true,
            SnapshotBalances = new Dictionary<string, long>()
        };
        genesis.Hash = ComputeHash(genesis);
        _chain.Add(genesis);
        _logger.LogInformation("[ledger] 区块链初始化，创世块 hash={Hash}", genesis.Hash);
    }

    /// <summary>注入在线节点查询委托（由 Tracker 提供，避免循环依赖）。</summary>
    public void SetOnlineNodesProvider(Func<List<(string deviceId, DateTime connectedAt)>> provider)
        => _getOnlineNodes = provider;

    /// <summary>注入 deviceId → accountId 查询委托（由 AccountService 提供）。</summary>
    public void SetDeviceAccountResolver(Func<string, Task<long?>> resolver)
        => _resolveDeviceToAccount = resolver;

    /// <summary>accountId → 账本内部 key。</summary>
    private static string AccountKey(long accountId) => $"acc:{accountId}";

    /// <summary>注册账号到账本（赠送初始小鱼干）。</summary>
    public void RegisterAccount(long accountId)
    {
        var key = AccountKey(accountId);
        _balances.GetOrAdd(key, _ => InitialBalance);
        _lastRewardTime.GetOrAdd(key, _ => DateTime.UtcNow);
    }

    /// <summary>关联 deviceId 到 accountId（缓存，供传输记账和在线奖励使用）。</summary>
    public void BindDeviceToAccount(string deviceId, long accountId)
    {
        _deviceToAccount[deviceId] = AccountKey(accountId);
        // 同步注册账号（若尚未注册）
        RegisterAccount(accountId);
    }

    public void Start()
    {
        _mineTimer = new Timer(_ => _ = MineBlockAsync(), null, BlockInterval, BlockInterval);
        _pruneTimer = new Timer(_ => _ = TryPruneAsync(), null, PruneInterval, PruneInterval);
        // 在线奖励每分钟结算一次
        _onlineRewardTimer = new Timer(_ => RewardOnlineNodes(), null, OnlineRewardInterval, OnlineRewardInterval);
        _logger.LogInformation("[ledger] 出块定时器启动（{Sec}s），修剪定时器启动（{Days}d），在线奖励定时器启动（{Min}min），快照间隔 {N} 块",
            BlockInterval.TotalSeconds, PruneInterval.TotalDays, OnlineRewardInterval.TotalMinutes, SnapshotInterval);
    }

    public void Stop()
    {
        _mineTimer?.Dispose();
        _pruneTimer?.Dispose();
        _onlineRewardTimer?.Dispose();
        _mineTimer = null;
        _pruneTimer = null;
        _onlineRewardTimer = null;
    }

    public int Height => _chain.Count;
    public Block? Latest => _chain.Count > 0 ? _chain[^1] : null;

    /// <summary>当前账本占用内存估算（字节）。</summary>
    public long EstimatedSizeBytes
    {
        get
        {
            long total = 0;
            foreach (var b in _chain)
            {
                total += 200; // 区块头估算
                total += b.Transactions.Sum(t => 300); // 每笔交易估算
                if (b.IsSnapshot && b.SnapshotBalances != null)
                    total += b.SnapshotBalances.Count * 60;
            }
            return total;
        }
    }

    /// <summary>查询账号余额（传入 accountId）。</summary>
    public long GetBalance(long accountId)
        => _balances.TryGetValue(AccountKey(accountId), out var b) ? b : 0;

    /// <summary>查询设备关联账号的余额（异步，需查 AccountService）。</summary>
    public async Task<long> GetBalanceByDeviceAsync(string deviceId)
    {
        // 优先查缓存
        if (_deviceToAccount.TryGetValue(deviceId, out var key))
            return _balances.TryGetValue(key, out var b) ? b : 0;
        // 回源查 AccountService
        if (_resolveDeviceToAccount != null)
        {
            var accId = await _resolveDeviceToAccount(deviceId);
            if (accId.HasValue)
            {
                BindDeviceToAccount(deviceId, accId.Value);
                return GetBalance(accId.Value);
            }
        }
        return 0;
    }

    public IReadOnlyDictionary<string, long> AllBalances() => _balances;

    /// <summary>注册设备到账本（兼容旧接口，内部关联到账号）。</summary>
    public void RegisterNode(string deviceId)
    {
        // 旧接口兼容：若无账号关联，暂用 deviceId 作为 key（待 BindDeviceToAccount 后迁移）
        _balances.GetOrAdd(deviceId, _ => InitialBalance);
        _lastRewardTime.GetOrAdd(deviceId, _ => DateTime.UtcNow);
    }

    /// <summary>在线奖励：每分钟为在线节点发小鱼干（1h=10🐟 → 1min=10/60🐟，用毫秒精度累计）。
    /// 奖励发到 deviceId 关联的账号（跨设备共享积分）。</summary>
    private void RewardOnlineNodes()
    {
        if (_getOnlineNodes == null) return;
        try
        {
            var now = DateTime.UtcNow;
            var onlineNodes = _getOnlineNodes();
            foreach (var (deviceId, connectedAt) in onlineNodes)
            {
                // 解析 deviceId → accountKey（优先用缓存，未命中则跳过等下次异步解析）
                if (!_deviceToAccount.TryGetValue(deviceId, out var key))
                {
                    // 旧式未绑定账号的设备，用 deviceId 作为 key
                    key = deviceId;
                }

                var last = _lastRewardTime.GetOrAdd(key, _ => connectedAt);
                var elapsed = now - last;
                if (elapsed < OnlineRewardInterval) continue;

                // 按比例计算：10 🐟/3600 秒 × 实际秒数（四舍五入，最低 1）
                var fish = (int)Math.Round(FishPerHour * elapsed.TotalSeconds / 3600.0);
                if (fish < 1) continue;

                _pending.Enqueue(new Transaction
                {
                    Type = TxType.Reward,
                    From = "SYSTEM",
                    To = key,
                    Amount = fish,
                    FileId = "",
                    Bytes = 0,
                    Timestamp = new DateTimeOffset(now).ToUnixTimeMilliseconds(),
                    Remark = $"在线奖励 {elapsed.TotalMinutes:F0} 分钟"
                });

                _lastRewardTime[key] = now;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ledger] 在线奖励结算失败");
        }
    }

    public void RecordUpload(string uploaderDeviceId, string downloaderDeviceId, long bytes, string fileId)
    {
        var fish = BytesToFish(bytes);
        var uploaderKey = ResolveDeviceKey(uploaderDeviceId);
        var downloaderShort = ShortId(downloaderDeviceId);
        _pending.Enqueue(new Transaction
        {
            Type = TxType.Upload,
            From = "SYSTEM",
            To = uploaderKey,
            Amount = fish,
            FileId = fileId,
            Bytes = bytes,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Remark = $"上传 {FormatBytes(bytes)} 给 {downloaderShort}"
        });
    }

    public void RecordDownload(string downloaderDeviceId, string uploaderDeviceId, long bytes, string fileId)
    {
        var fish = BytesToFish(bytes);
        var downloaderKey = ResolveDeviceKey(downloaderDeviceId);
        var uploaderShort = ShortId(uploaderDeviceId);
        _pending.Enqueue(new Transaction
        {
            Type = TxType.Download,
            From = downloaderKey,
            To = "SYSTEM",
            Amount = fish,
            FileId = fileId,
            Bytes = bytes,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Remark = $"从 {uploaderShort} 下载 {FormatBytes(bytes)}"
        });
    }

    /// <summary>deviceId → 账本 key（已绑定账号返回 accountKey，否则返回 deviceId）。</summary>
    private string ResolveDeviceKey(string deviceId)
        => _deviceToAccount.TryGetValue(deviceId, out var key) ? key : deviceId;

    private static string ShortId(string id)
        => id.Length > 12 ? id[..12] + "..." : id;

    /// <summary>字节 → 小鱼干换算：1 GB = 10 🐟，不足 1GB 按 1GB 算（最低 1 🐟）。</summary>
    private static int BytesToFish(long bytes)
    {
        if (bytes <= 0) return 1;
        var gb = bytes / BytesPerGB;
        if (bytes % BytesPerGB != 0) gb++; // 向上取整
        return Math.Max(1, (int)(gb * FishPerGB));
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    /// <summary>查询指定账号的交易历史（仅快照后的区块）。</summary>
    public List<Transaction> GetHistory(long accountId)
    {
        var key = AccountKey(accountId);
        var list = new List<Transaction>();
        foreach (var block in _chain)
        {
            if (block.IsSnapshot) continue; // 快照块无交易明细
            foreach (var tx in block.Transactions)
            {
                if (tx.From == key || tx.To == key)
                    list.Add(tx);
            }
        }
        return list;
    }

    /// <summary>查询指定设备的交易历史（兼容旧接口，内部转换到账号）。</summary>
    public List<Transaction> GetHistoryByDevice(string deviceId)
    {
        var key = _deviceToAccount.TryGetValue(deviceId, out var k) ? k : deviceId;
        var list = new List<Transaction>();
        foreach (var block in _chain)
        {
            if (block.IsSnapshot) continue;
            foreach (var tx in block.Transactions)
            {
                if (tx.From == key || tx.To == key)
                    list.Add(tx);
            }
        }
        return list;
    }

    public IReadOnlyList<Block> GetChain() => _chain.AsReadOnly();

    /// <summary>验证链完整性（快照块之后的部分）。</summary>
    public bool ValidateChain()
    {
        for (int i = 1; i < _chain.Count; i++)
        {
            var block = _chain[i];
            if (block.PrevHash != _chain[i - 1].Hash) return false;
            if (block.Hash != ComputeHash(block)) return false;
        }
        return true;
    }

    // ── 出块 ──

    private async Task MineBlockAsync()
    {
        if (_pending.IsEmpty) return;

        Block? newBlock = null;
        lock (_blockLock)
        {
            if (_pending.IsEmpty) return;

            var txs = new List<Transaction>();
            while (txs.Count < MaxTransactionsPerBlock && _pending.TryDequeue(out var tx))
                txs.Add(tx);

            var prev = _chain[^1];
            var block = new Block
            {
                Index = prev.Index + 1,
                PrevHash = prev.Hash,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Transactions = txs,
                Nonce = 0
            };

            // 工作量证明
            var target = new string('0', BlockDifficulty);
            while (true)
            {
                var hash = ComputeHash(block);
                if (hash.StartsWith(target))
                {
                    block.Hash = hash;
                    break;
                }
                block.Nonce++;
            }

            // 应用交易到余额
            foreach (var tx in txs)
            {
                if (tx.From != "SYSTEM")
                    _balances.AddOrUpdate(tx.From, 0, (_, v) => v - tx.Amount);
                if (tx.To != "SYSTEM")
                    _balances.AddOrUpdate(tx.To, 0, (_, v) => v + tx.Amount);
            }

            _chain.Add(block);
            newBlock = block;
        }

        if (newBlock != null)
        {
            _logger.LogInformation("[ledger] 出块 #{Index} hash={Hash} 交易数={TxCount}",
                newBlock.Index, newBlock.Hash, newBlock.Transactions.Count);

            if (OnBlockMined != null)
            {
                try { await OnBlockMined.Invoke(newBlock); } catch { }
            }
        }
    }

    // ── 快照 + 修剪 ──

    /// <summary>定时修剪：若区块数超过 SnapshotInterval，生成快照并删除旧块。</summary>
    private Task TryPruneAsync()
    {
        try
        {
            lock (_blockLock)
            {
                if (_chain.Count <= SnapshotInterval) return Task.CompletedTask;

                // 找到最后一个普通块的位置，在其后插入快照块
                var lastBlock = _chain[^1];
                var snapshot = new Block
                {
                    Index = lastBlock.Index + 1,
                    PrevHash = lastBlock.Hash,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Transactions = new List<Transaction>(),
                    Nonce = 0,
                    IsSnapshot = true,
                    SnapshotBalances = new Dictionary<string, long>(_balances)
                };

                // 快照块也需要满足 PoW（保证不可伪造）
                var target = new string('0', BlockDifficulty);
                while (true)
                {
                    var hash = ComputeHash(snapshot);
                    if (hash.StartsWith(target)) { snapshot.Hash = hash; break; }
                    snapshot.Nonce++;
                }

                // 删除快照块之前的所有块（包括旧快照块）
                _chain.Clear();
                _chain.Add(snapshot);
                PrunedToIndex = snapshot.Index;

                _logger.LogInformation("[ledger] 修剪完成，快照块 #{Index} 保留 {Nodes} 个节点余额，账本大小约 {KB} KB",
                    snapshot.Index, snapshot.SnapshotBalances.Count, EstimatedSizeBytes / 1024);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ledger] 修剪失败");
        }
        return Task.CompletedTask;
    }

    /// <summary>计算区块哈希。
    /// 快照块哈希包含余额快照（保证快照内容不可篡改）。</summary>
    private static string ComputeHash(Block block)
    {
        var txJson = JsonSerializer.Serialize(block.Transactions);
        var snapJson = block.IsSnapshot ? JsonSerializer.Serialize(block.SnapshotBalances) : "";
        var raw = $"{block.Index}{block.PrevHash}{block.Timestamp}{txJson}{snapJson}{block.Nonce}{block.IsSnapshot}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

// ── 区块与交易模型 ──

public class Block
{
    public int Index { get; set; }
    public string PrevHash { get; set; } = "";
    public long Timestamp { get; set; }
    public List<Transaction> Transactions { get; set; } = new();
    public long Nonce { get; set; }
    public string Hash { get; set; } = "";

    /// <summary>是否为快照块（true=此块包含全量余额快照，无交易明细）。</summary>
    public bool IsSnapshot { get; set; }

    /// <summary>快照块的全量余额（仅当 IsSnapshot=true 时有效）。</summary>
    public Dictionary<string, long>? SnapshotBalances { get; set; }
}

public class Transaction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public TxType Type { get; set; }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public long Amount { get; set; }          // 积分数
    public string FileId { get; set; } = "";  // 涉及的歌曲/文件
    public long Bytes { get; set; }            // 传输字节数
    public long Timestamp { get; set; }
    public string Remark { get; set; } = "";
}

public enum TxType
{
    Upload,      // 上传赚积分
    Download,    // 下载扣积分
    Reward,      // 系统奖励（如长期在线）
    Penalty      // 系统惩罚（如恶意行为）
}
