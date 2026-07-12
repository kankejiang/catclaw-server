using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace CatClawMusicServer.ClawCircle;

/// <summary>
/// Bloom Filter — 用于压缩歌曲索引，使节点可以高效广播"我是否有这首歌"。
/// 误判率可控（默认 1%），空间效率极高。
/// </summary>
public class BloomFilter
{
    private readonly BitArray _bits;
    private readonly int _hashCount;
    private readonly int _size;

    /// <param name="expectedItems">预期插入元素数量</param>
    /// <param name="falsePositiveRate">期望误判率（0.01 = 1%）</param>
    public BloomFilter(int expectedItems = 10000, double falsePositiveRate = 0.01)
    {
        // 最优大小: m = -n * ln(p) / (ln(2))^2
        _size = (int)Math.Ceiling(-expectedItems * Math.Log(falsePositiveRate) / (Math.Log(2) * Math.Log(2)));
        // 最优哈希数: k = m/n * ln(2)
        _hashCount = Math.Max(1, (int)Math.Round((double)_size / expectedItems * Math.Log(2)));
        _bits = new BitArray(_size);
    }

    private BloomFilter(byte[] data, int hashCount)
    {
        _bits = new BitArray(data);
        _size = _bits.Length;
        _hashCount = hashCount;
    }

    /// <summary>添加元素</summary>
    public void Add(string item)
    {
        var bytes = Encoding.UTF8.GetBytes(item);
        for (int i = 0; i < _hashCount; i++)
        {
            var hash = ComputeHash(bytes, i);
            _bits[hash % _size] = true;
        }
    }

    /// <summary>批量添加</summary>
    public void AddRange(IEnumerable<string> items)
    {
        foreach (var item in items) Add(item);
    }

    /// <summary>检查元素是否可能存在（可能误判，不会漏判）</summary>
    public bool Contains(string item)
    {
        var bytes = Encoding.UTF8.GetBytes(item);
        for (int i = 0; i < _hashCount; i++)
        {
            var hash = ComputeHash(bytes, i);
            if (!_bits[hash % _size]) return false;
        }
        return true;
    }

    /// <summary>导出为可序列化的数据</summary>
    public BloomFilterData Export()
    {
        var data = new byte[(_size + 7) / 8];
        _bits.CopyTo(data, 0);
        return new BloomFilterData { Bits = Convert.ToBase64String(data), HashCount = _hashCount, Size = _size };
    }

    /// <summary>从序列化数据恢复</summary>
    public static BloomFilter Import(BloomFilterData data)
    {
        var bytes = Convert.FromBase64String(data.Bits);
        return new BloomFilter(bytes, data.HashCount);
    }

    /// <summary>估算填充率</summary>
    public double FillRate
    {
        get
        {
            int set = 0;
            for (int i = 0; i < _size; i++)
                if (_bits[i]) set++;
            return (double)set / _size;
        }
    }

    private static int ComputeHash(byte[] data, int seed)
    {
        // 使用 SHA256 的前 4 字节作为哈希，加 seed 作为变体
        using var sha = SHA256.Create();
        var input = new byte[data.Length + 4];
        Buffer.BlockCopy(data, 0, input, 0, data.Length);
        BitConverter.GetBytes(seed).CopyTo(input, data.Length);
        var hash = sha.ComputeHash(input);
        return BitConverter.ToInt32(hash, 0) & 0x7FFFFFFF; // 确保非负
    }
}

/// <summary>Bloom Filter 序列化格式</summary>
public class BloomFilterData
{
    [JsonPropertyName("bits")]
    public string Bits { get; set; } = "";

    [JsonPropertyName("k")]
    public int HashCount { get; set; }

    [JsonPropertyName("m")]
    public int Size { get; set; }
}
