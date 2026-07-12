using System.Collections.Concurrent;

namespace CatClawMusicServer.ClawCircle;

/// <summary>
/// 节点信誉系统 — 跟踪每个节点的可靠性，用于防范恶意节点。
/// 信誉值 0-100，初始 50。成功传输加分，失败/超时减分。
/// 信誉低于阈值的节点会被降权或排除。
/// </summary>
public class NodeReputation
{
    public const int MinReputation = 0;
    public const int MaxReputation = 100;
    public const int DefaultReputation = 50;
    public const int BlacklistThreshold = 10; // 低于此值被黑名单
    public const int TrustedThreshold = 80;    // 高于此值视为可信

    private readonly ConcurrentDictionary<string, ReputationRecord> _records = new();

    /// <summary>获取节点信誉</summary>
    public int GetReputation(string nodeId)
    {
        if (_records.TryGetValue(nodeId, out var record))
            return record.Score;
        return DefaultReputation;
    }

    /// <summary>记录成功交互</summary>
    public void RecordSuccess(string nodeId, int points = 2)
    {
        var record = _records.GetOrAdd(nodeId, _ => new ReputationRecord());
        record.Score = Math.Min(MaxReputation, record.Score + points);
        record.SuccessCount++;
        record.LastSeen = DateTime.UtcNow;
    }

    /// <summary>记录失败交互</summary>
    public void RecordFailure(string nodeId, int points = 5)
    {
        var record = _records.GetOrAdd(nodeId, _ => new ReputationRecord());
        record.Score = Math.Max(MinReputation, record.Score - points);
        record.FailureCount++;
        record.LastSeen = DateTime.UtcNow;
    }

    /// <summary>记录超时</summary>
    public void RecordTimeout(string nodeId)
    {
        RecordFailure(nodeId, 3);
    }

    /// <summary>检查节点是否被黑名单</summary>
    public bool IsBlacklisted(string nodeId) => GetReputation(nodeId) < BlacklistThreshold;

    /// <summary>检查节点是否可信</summary>
    public bool IsTrusted(string nodeId) => GetReputation(nodeId) >= TrustedThreshold;

    /// <summary>获取信誉摘要</summary>
    public ReputationRecord? GetRecord(string nodeId)
        => _records.TryGetValue(nodeId, out var r) ? r : null;

    /// <summary>清理过期记录（30天未见）</summary>
    public void Cleanup()
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);
        foreach (var kv in _records)
        {
            if (kv.Value.LastSeen < cutoff)
                _records.TryRemove(kv.Key, out _);
        }
    }

    /// <summary>所有记录摘要</summary>
    public IReadOnlyDictionary<string, ReputationRecord> All()
        => _records;
}

public class ReputationRecord
{
    public int Score { get; set; } = NodeReputation.DefaultReputation;
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    public double SuccessRate
    {
        get
        {
            var total = SuccessCount + FailureCount;
            return total > 0 ? (double)SuccessCount / total : 0;
        }
    }
}
