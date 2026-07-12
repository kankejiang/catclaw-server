using System.Collections.Concurrent;
using System.Net;

namespace CatClawMusicServer.ClawCircle.Dht;

/// <summary>
/// Kademlia K-Bucket 路由表。
/// 按 XOR 距离将已知节点分入 160 个桶，每桶最多 K 个节点。
/// </summary>
public class RoutingTable
{
    public const int K = 20;           // 每桶最大节点数
    public const int Alpha = 3;        // 并发查询数
    public const int BucketCount = 160; // 160 位 NodeId

    private readonly NodeId _localId;
    private readonly List<KBucket> _buckets;
    private readonly ILogger<RoutingTable> _logger;

    public NodeId LocalId => _localId;
    public int TotalNodes => _buckets.Sum(b => b.Count);

    public RoutingTable(NodeId localId, ILogger<RoutingTable> logger)
    {
        _localId = localId;
        _logger = logger;
        _buckets = new List<KBucket>(BucketCount);
        for (int i = 0; i < BucketCount; i++)
            _buckets.Add(new KBucket());
    }

    /// <summary>添加或更新节点（返回 true 表示成功添加）</summary>
    public bool AddOrUpdate(DhtNode node)
    {
        if (node.Id == _localId) return false;

        var idx = _localId.BucketIndex(node.Id);
        if (idx < 0 || idx >= BucketCount) return false;

        return _buckets[idx].AddOrUpdate(node);
    }

    /// <summary>移除节点</summary>
    public bool Remove(NodeId id)
    {
        if (id == _localId) return false;
        var idx = _localId.BucketIndex(id);
        if (idx < 0 || idx >= BucketCount) return false;
        return _buckets[idx].Remove(id);
    }

    /// <summary>查找距离目标最近的 K 个节点</summary>
    public List<DhtNode> FindClosest(NodeId target, int count = K)
    {
        var allNodes = _buckets.SelectMany(b => b.GetAll()).ToList();
        return allNodes
            .OrderBy(n => target.XorDistance(n.Id).CompareTo(NodeId.Random())) // 按 XOR 距离排序
            .Take(count)
            .ToList();
    }

    /// <summary>获取指定桶的节点</summary>
    public List<DhtNode> GetBucket(int index)
    {
        if (index < 0 || index >= BucketCount) return new List<DhtNode>();
        return _buckets[index].GetAll();
    }

    /// <summary>获取所有已知节点</summary>
    public List<DhtNode> GetAllNodes()
        => _buckets.SelectMany(b => b.GetAll()).ToList();

    /// <summary>路由表摘要（用于调试）</summary>
    public string Summary()
    {
        var nonEmpty = _buckets.Where(b => b.Count > 0).ToList();
        return $"RoutingTable: {TotalNodes} nodes in {nonEmpty.Count}/{BucketCount} buckets";
    }
}

/// <summary>单个 K-Bucket — 最多 K 个节点，LRU 淘汰。</summary>
public class KBucket
{
    private readonly List<DhtNode> _nodes = new();
    private readonly object _lock = new();

    public int Count { get { lock (_lock) return _nodes.Count; } }

    public bool AddOrUpdate(DhtNode node)
    {
        lock (_lock)
        {
            var existing = _nodes.FindIndex(n => n.Id == node.Id);
            if (existing >= 0)
            {
                // 更新已有节点（移到末尾 = 最近使用）
                _nodes.RemoveAt(existing);
                node.LastSeen = DateTime.UtcNow;
                _nodes.Add(node);
                return true;
            }

            if (_nodes.Count < RoutingTable.K)
            {
                _nodes.Add(node);
                return true;
            }

            // 桶已满 — 检查最旧节点是否过期（超过 15 分钟未见）
            var oldest = _nodes[0];
            if ((DateTime.UtcNow - oldest.LastSeen).TotalMinutes > 15)
            {
                _nodes.RemoveAt(0);
                _nodes.Add(node);
                return true;
            }

            return false; // 桶满且无过期节点
        }
    }

    public bool Remove(NodeId id)
    {
        lock (_lock)
        {
            var idx = _nodes.FindIndex(n => n.Id == id);
            if (idx >= 0) { _nodes.RemoveAt(idx); return true; }
            return false;
        }
    }

    public List<DhtNode> GetAll()
    {
        lock (_lock) return new List<DhtNode>(_nodes);
    }
}

/// <summary>DHT 网络中的节点信息。</summary>
public class DhtNode
{
    public NodeId Id { get; set; }
    public IPEndPoint Endpoint { get; set; } = new(IPAddress.Loopback, 0);
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public int Reputation { get; set; } = 50; // 0-100，初始信誉 50
    public bool IsAlive => (DateTime.UtcNow - LastSeen).TotalMinutes < 30;
}
