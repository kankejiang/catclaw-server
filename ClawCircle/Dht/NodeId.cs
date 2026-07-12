using System.Security.Cryptography;
using System.Text;

namespace CatClawMusicServer.ClawCircle.Dht;

/// <summary>
/// 160 位 Kademlia 节点 ID — 基于 SHA-1 哈希。
/// 支持 XOR 距离计算（Kademlia 核心）。
/// </summary>
public readonly struct NodeId : IEquatable<NodeId>, IComparable<NodeId>
{
    public const int Length = 20; // 160 bits = 20 bytes
    public const int HexLength = Length * 2;

    private readonly byte[] _bytes;

    public ReadOnlySpan<byte> Bytes => _bytes;

    public NodeId(byte[] bytes)
    {
        if (bytes.Length != Length)
            throw new ArgumentException($"NodeId must be {Length} bytes", nameof(bytes));
        _bytes = (byte[])bytes.Clone();
    }

    /// <summary>从字符串生成 NodeId（SHA-1 哈希）</summary>
    public static NodeId FromString(string s)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(s));
        return new NodeId(hash);
    }

    /// <summary>从十六进制字符串解析</summary>
    public static NodeId FromHex(string hex)
    {
        if (hex.Length != HexLength)
            throw new ArgumentException($"Hex must be {HexLength} chars", nameof(hex));
        var bytes = Convert.FromHexString(hex);
        return new NodeId(bytes);
    }

    /// <summary>生成随机 NodeId</summary>
    public static NodeId Random()
    {
        var bytes = new byte[Length];
        RandomNumberGenerator.Fill(bytes);
        return new NodeId(bytes);
    }

    /// <summary>XOR 距离（核心度量）— 值越小越近</summary>
    public NodeId XorDistance(NodeId other)
    {
        var result = new byte[Length];
        for (int i = 0; i < Length; i++)
            result[i] = (byte)(_bytes[i] ^ other._bytes[i]);
        return new NodeId(result);
    }

    /// <summary>XOR 距离的桶索引 — 最高有效位位置（0 = 自身，159 = 最远）</summary>
    public int BucketIndex(NodeId other)
    {
        var dist = XorDistance(other);
        for (int i = 0; i < Length; i++)
        {
            if (dist._bytes[i] == 0) continue;
            // 找到第一个非零字节的最高位
            int leading = System.Numerics.BitOperations.LeadingZeroCount((uint)dist._bytes[i]) - 24;
            return (Length - 1 - i) * 8 + (7 - leading);
        }
        return -1; // 相同节点
    }

    /// <summary>比较两个距离（用于排序）</summary>
    public int CompareTo(NodeId other)
    {
        for (int i = 0; i < Length; i++)
        {
            var cmp = _bytes[i].CompareTo(other._bytes[i]);
            if (cmp != 0) return cmp;
        }
        return 0;
    }

    public bool Equals(NodeId other) => CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is NodeId id && Equals(id);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        for (int i = 0; i < Math.Min(8, Length); i++)
            hash.Add(_bytes[i]);
        return hash.ToHashCode();
    }

    public override string ToString() => Convert.ToHexString(_bytes).ToLowerInvariant();

    public static bool operator ==(NodeId a, NodeId b) => a.Equals(b);
    public static bool operator !=(NodeId a, NodeId b) => !a.Equals(b);
}
