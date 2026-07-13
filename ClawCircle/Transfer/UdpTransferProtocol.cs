using System.Collections.Concurrent;
using System.Net;
using System.Text;
using CatClawMusicServer.ClawCircle.Ledger;

namespace CatClawMusicServer.ClawCircle.Transfer;

/// <summary>
/// UDP P2P 传输协议 — 在 STUN UDP socket 上多路复用，与 STUN 反射包共享同一端口
/// （这是 NAT 打洞的硬性要求：STUN 观察到的反射端点必须就是后续传输用的端点）。
///
/// 帧格式（二进制，首字节 0x01-0x0F 标识消息类型，与 STUN JSON 包首字节 '{'=0x7B 互斥）：
///   [1 字节: 类型] [2 字节: taskId 长度 N] [N 字节: taskId UTF-8] [类型相关字段...] [数据]
///
/// 协议设计为「接收方驱动」：接收方知道缺失哪些 chunk，主动发 ChunkRequest；
/// 发送方响应 ChunkSegment（256KB chunk 再切为 ~1400 字节 segment 避免 IP 分片）；
/// 接收方组装回完整 chunk 后校验 SHA256，发 ChunkAck 或 ChunkNack 触发重传。
/// 这种 pull 模式天然处理丢包，无需发送方维护复杂状态。
/// </summary>
public class UdpTransferProtocol
{
    // 消息类型（首字节，避开 '{'=0x7B 与 '}'=0x7D）
    public const byte MsgChunkSegment = 0x01; // 数据分片
    public const byte MsgChunkRequest = 0x02;  // 请求某 chunk（接收方→发送方）
    public const byte MsgChunkAck = 0x03;       // chunk 校验通过
    public const byte MsgChunkNack = 0x04;      // chunk 校验失败，请重传
    public const byte MsgHolePunch = 0x05;      // NAT 打洞包（无业务数据）
    public const byte MsgPing = 0x06;            // 连通性探测
    public const byte MsgPong = 0x07;            // 连通性探测响应
    public const byte MsgTransferEnd = 0x08;     // 传输结束（双向）

    /// <summary>UDP segment 最大载荷（字节）。1400 留足 MTU 余量，避免 IP 分片。</summary>
    public const int SegmentPayload = 1400;

    /// <summary>chunk 重组缓冲区：taskId + chunkIndex → 已收到的 segments。</summary>
    private readonly ConcurrentDictionary<(string taskId, int chunkIndex), SegmentReassembly> _reassembly = new();

    /// <summary>每个传输任务的发送方上下文：taskId → 发送回调（用于响应 ChunkRequest）。</summary>
    private readonly ConcurrentDictionary<string, SenderContext> _senders = new();

    /// <summary>每个传输任务的接收方上下文：taskId → 接收方状态（含 TransferEngine 任务 ID）。</summary>
    private readonly ConcurrentDictionary<string, ReceiverContext> _receivers = new();

    /// <summary>NAT 打洞探测：最近收到的打洞包源端点（taskId → 远端反射端点）。</summary>
    private readonly ConcurrentDictionary<string, IPEndPoint> _holePunchEndpoints = new();

    private readonly TransferEngine _engine;
    private readonly NodeReputation _reputation;
    private readonly BlockchainLedger _ledger;
    private readonly ILogger<UdpTransferProtocol> _logger;

    // 由 STUN 服务注入的发送委托（共享同一个 UDP socket）
    private Func<IPEndPoint, byte[], Task>? _sendFunc;

    public UdpTransferProtocol(
        TransferEngine engine,
        NodeReputation reputation,
        BlockchainLedger ledger,
        ILogger<UdpTransferProtocol> logger)
    {
        _engine = engine;
        _reputation = reputation;
        _ledger = ledger;
        _logger = logger;
    }

    /// <summary>由 STUN 服务注入 UDP 发送能力（共享 socket）。</summary>
    public void BindSendFunc(Func<IPEndPoint, byte[], Task> sendFunc) => _sendFunc = sendFunc;

    /// <summary>注册发送方上下文（发送方在 WebSocket 协商 transfer_accept 后调用）。</summary>
    public void RegisterSender(string taskId, string filePath, string peerDeviceId,
        PieceManifest? manifest = null, IPEndPoint? peerEndpoint = null)
    {
        _senders[taskId] = new SenderContext
        {
            TaskId = taskId,
            FilePath = filePath,
            PeerDeviceId = peerDeviceId,
            Manifest = manifest,
            PeerEndpoint = peerEndpoint
        };
    }

    /// <summary>注册接收方上下文（接收方在 WebSocket 协商 transfer_request 时调用）。</summary>
    public void RegisterReceiver(string taskId, string localEngineTaskId, string peerDeviceId,
        IPEndPoint? peerEndpoint = null)
    {
        _receivers[taskId] = new ReceiverContext
        {
            TaskId = taskId,
            EngineTaskId = localEngineTaskId,
            PeerDeviceId = peerDeviceId,
            PeerEndpoint = peerEndpoint
        };
    }

    /// <summary>记录 NAT 打洞来源（接收方收到打洞包后可据此反向打洞）。</summary>
    public IPEndPoint? GetHolePunchEndpoint(string taskId)
        => _holePunchEndpoints.TryGetValue(taskId, out var ep) ? ep : null;

    /// <summary>STUN 服务收到 UDP 包时调用此方法分发。返回 true 表示已作为传输包处理。</summary>
    public async Task<bool> HandlePacketAsync(byte[] data, IPEndPoint remote)
    {
        if (data.Length == 0) return false;
        var firstByte = data[0];

        // STUN JSON 包首字节是 '{' (0x7B)，传输包首字节 0x01-0x08
        if (firstByte is < 0x01 or > 0x08) return false;

        try
        {
            switch (firstByte)
            {
                case MsgChunkSegment:
                    await HandleChunkSegmentAsync(data, remote);
                    break;
                case MsgChunkRequest:
                    await HandleChunkRequestAsync(data, remote);
                    break;
                case MsgChunkAck:
                    HandleChunkAck(data);
                    break;
                case MsgChunkNack:
                    await HandleChunkNackAsync(data, remote);
                    break;
                case MsgHolePunch:
                    HandleHolePunch(data, remote);
                    break;
                case MsgPing:
                    var pingTaskId = ExtractTaskId(data);
                    if (pingTaskId != null)
                        await SendAsync(remote, EncodeSimple(MsgPong, pingTaskId));
                    break;
                case MsgPong:
                    // 连通性确认，由 Ping 发起方在 _pingWaits 中处理（简化：仅记录日志）
                    _logger.LogDebug("[udp-transfer] Pong from {Remote}", remote);
                    break;
                case MsgTransferEnd:
                    HandleTransferEnd(data);
                    break;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[udp-transfer] 处理包失败 from {Remote}", remote);
            return false;
        }
    }

    // ── 发送方：响应 ChunkRequest，将整个 chunk 切片为 segment 发送 ──
    private async Task HandleChunkRequestAsync(byte[] data, IPEndPoint remote)
    {
        var (taskId, chunkIndex) = DecodeChunkRef(data);
        if (taskId == null) return;
        if (!_senders.TryGetValue(taskId, out var ctx)) return;

        var chunk = await _engine.ReadChunkAsync(ctx.FilePath, chunkIndex);
        if (chunk == null) return;

        // 将 chunk 切片为 segment
        var totalSegments = (int)Math.Ceiling((double)chunk.Length / SegmentPayload);
        for (int seg = 0; seg < totalSegments; seg++)
        {
            var offset = seg * SegmentPayload;
            var len = Math.Min(SegmentPayload, chunk.Length - offset);
            var segData = new byte[len];
            Array.Copy(chunk, offset, segData, 0, len);

            var frame = EncodeChunkSegment(taskId, chunkIndex, seg, totalSegments, segData);
            await SendAsync(remote, frame);

            // 简单流控：每发 8 个 segment 让出一次，避免打爆接收方
            if ((seg & 0x07) == 7)
                await Task.Delay(1);
        }
    }

    // ── 接收方：收到 chunk segment，重组完整 chunk 后校验写入 ──
    private async Task HandleChunkSegmentAsync(byte[] data, IPEndPoint remote)
    {
        var seg = DecodeChunkSegment(data);
        if (seg == null) return;
        if (!_receivers.TryGetValue(seg.TaskId, out var ctx)) return;

        var key = (seg.TaskId, seg.ChunkIndex);
        var buf = _reassembly.GetOrAdd(key, _ => new SegmentReassembly(seg.TotalSegments));

        if (!buf.SetSegment(seg.SegmentIndex, seg.Data))
        {
            // 重复或越界 segment，忽略
            return;
        }

        if (!buf.IsComplete) return;

        // 所有 segment 收齐，组装完整 chunk
        var fullChunk = buf.Assemble();
        _reassembly.TryRemove(key, out _);

        // 交给 TransferEngine 校验和写入
        var ok = _engine.ReceiveChunk(ctx.EngineTaskId, seg.ChunkIndex, fullChunk);
        if (ok)
        {
            ctx.ReceivedBytes += fullChunk.Length;
            _reputation.RecordSuccess(ctx.PeerDeviceId, 1);
            await SendAsync(remote, EncodeChunkRef(MsgChunkAck, seg.TaskId, seg.ChunkIndex));

            // 全部 chunk 收齐 → 记入账本（下载方消耗积分）
            var task = _engine.GetTask(ctx.EngineTaskId);
            if (!ctx.LedgerRecorded && task != null && task.Status == TransferStatus.Complete)
            {
                ctx.LedgerRecorded = true;
                _ledger.RecordDownload("local", ctx.PeerDeviceId, ctx.ReceivedBytes, task.Manifest.FileName);
                _logger.LogInformation("[udp-transfer] 接收方记账 taskId={TaskId} bytes={Bytes}", seg.TaskId, ctx.ReceivedBytes);
            }
        }
        else
        {
            _reputation.RecordFailure(ctx.PeerDeviceId, 2);
            await SendAsync(remote, EncodeChunkRef(MsgChunkNack, seg.TaskId, seg.ChunkIndex));
            _logger.LogWarning("[udp-transfer] chunk {Index} 校验失败，请求重传", seg.ChunkIndex);
        }
    }

    // ── 接收方：发送方收到 ACK 后累计字节，全部确认时记账（上传方赚积分） ──
    private void HandleChunkAck(byte[] data)
    {
        var (taskId, chunkIndex) = DecodeChunkRef(data);
        if (taskId == null || !_senders.TryGetValue(taskId, out var ctx)) return;

        ctx.AckedChunks++;
        // 累计此 chunk 的字节数
        if (ctx.Manifest != null && chunkIndex >= 0 && chunkIndex < ctx.Manifest.Chunks.Count)
            ctx.SentBytes += ctx.Manifest.Chunks[chunkIndex].Size;

        // 全部 chunk 确认 → 记入账本（上传方获得积分）
        if (!ctx.LedgerRecorded && ctx.Manifest != null && ctx.AckedChunks >= ctx.Manifest.TotalChunks)
        {
            ctx.LedgerRecorded = true;
            _ledger.RecordUpload(ctx.PeerDeviceId, "remote", ctx.SentBytes, ctx.Manifest.FileName);
            _logger.LogInformation("[udp-transfer] 发送方记账 taskId={TaskId} bytes={Bytes}", taskId, ctx.SentBytes);
        }
    }

    // ── 发送方：收到 NACK，立即重传对应 chunk（接收方驱动重传） ──
    private async Task HandleChunkNackAsync(byte[] data, IPEndPoint remote)
    {
        // 重传逻辑与 ChunkRequest 相同：重新发送整个 chunk 的所有 segment
        await HandleChunkRequestAsync(data, remote);
    }

    // ── NAT 打洞包：记录来源，便于接收方反向打洞 ──
    private void HandleHolePunch(byte[] data, IPEndPoint remote)
    {
        var taskId = ExtractTaskId(data);
        if (taskId == null) return;
        _holePunchEndpoints[taskId] = remote;
        _logger.LogInformation("[udp-transfer] 收到打洞包 taskId={TaskId} from {Remote}", taskId, remote);
    }

    private void HandleTransferEnd(byte[] data)
    {
        var taskId = ExtractTaskId(data);
        if (taskId == null) return;
        _senders.TryRemove(taskId, out _);
        _receivers.TryRemove(taskId, out _);
        _holePunchEndpoints.TryRemove(taskId, out _);
        // 清理未完成的重组缓冲
        var keysToRemove = _reassembly.Keys.Where(k => k.taskId == taskId).ToList();
        foreach (var k in keysToRemove) _reassembly.TryRemove(k, out _);
        _logger.LogInformation("[udp-transfer] 传输结束 taskId={TaskId}", taskId);
    }

    // ── 主动发送：打洞包 ──
    public async Task SendHolePunchAsync(IPEndPoint target, string taskId)
    {
        var frame = EncodeSimple(MsgHolePunch, taskId);
        // 连发 3 次提高打洞成功率
        for (int i = 0; i < 3; i++)
            await SendAsync(target, frame);
    }

    // ── 主动发送：连通性探测 ──
    public async Task<bool> PingAsync(IPEndPoint target, string taskId, int timeoutMs = 2000)
    {
        var frame = EncodeSimple(MsgPing, taskId);
        var tcs = new TaskCompletionSource<bool>();
        // 简化：发 Ping 后等 Pong，超时即失败。完整实现需要 Pong 路由到 tcs。
        // 此处仅发送，连通性由后续数据传输隐式验证。
        await SendAsync(target, frame);
        return true;
    }

    /// <summary>结束传输任务（清理资源并通知对方）。</summary>
    public async Task EndTransferAsync(IPEndPoint? peer, string taskId)
    {
        if (peer != null)
        {
            try { await SendAsync(peer, EncodeSimple(MsgTransferEnd, taskId)); } catch { }
        }
        HandleTransferEnd(EncodeSimple(MsgTransferEnd, taskId));
    }

    // ── 公共查询/管理 API（供 ClawCircleController 暴露） ──

    /// <summary>列出所有进行中的传输任务（发送 + 接收）。</summary>
    public List<TransferStatusInfo> GetAllTransfers()
    {
        var list = new List<TransferStatusInfo>();
        foreach (var kv in _senders)
        {
            var ctx = kv.Value;
            list.Add(new TransferStatusInfo
            {
                TaskId = ctx.TaskId,
                Role = "sender",
                PeerDeviceId = ctx.PeerDeviceId,
                FileName = ctx.Manifest?.FileName ?? Path.GetFileName(ctx.FilePath),
                TotalSize = ctx.Manifest?.TotalSize ?? 0,
                TotalChunks = ctx.Manifest?.TotalChunks ?? 0,
                ReceivedChunks = ctx.AckedChunks,
                Progress = ctx.Manifest is { TotalChunks: > 0 }
                    ? (double)ctx.AckedChunks / ctx.Manifest.TotalChunks : 0,
                Status = "active",
                StartedAt = ctx.StartedAt
            });
        }
        foreach (var kv in _receivers)
        {
            var ctx = kv.Value;
            var task = _engine.GetTask(ctx.EngineTaskId);
            list.Add(new TransferStatusInfo
            {
                TaskId = ctx.TaskId,
                Role = "receiver",
                PeerDeviceId = ctx.PeerDeviceId,
                FileName = task?.Manifest.FileName ?? "",
                TotalSize = task?.Manifest.TotalSize ?? 0,
                TotalChunks = task?.Manifest.TotalChunks ?? 0,
                ReceivedChunks = task?.ReceivedCount ?? 0,
                Progress = task?.Progress ?? 0,
                Status = task?.Status.ToString().ToLowerInvariant() ?? "active",
                StartedAt = ctx.StartedAt
            });
        }
        return list;
    }

    /// <summary>查询单个传输任务状态。</summary>
    public TransferStatusInfo? GetTransfer(string taskId)
    {
        if (_senders.TryGetValue(taskId, out var sctx))
        {
            return new TransferStatusInfo
            {
                TaskId = sctx.TaskId,
                Role = "sender",
                PeerDeviceId = sctx.PeerDeviceId,
                FileName = sctx.Manifest?.FileName ?? Path.GetFileName(sctx.FilePath),
                TotalSize = sctx.Manifest?.TotalSize ?? 0,
                TotalChunks = sctx.Manifest?.TotalChunks ?? 0,
                ReceivedChunks = sctx.AckedChunks,
                Progress = sctx.Manifest is { TotalChunks: > 0 }
                    ? (double)sctx.AckedChunks / sctx.Manifest.TotalChunks : 0,
                Status = "active",
                StartedAt = sctx.StartedAt
            };
        }
        if (_receivers.TryGetValue(taskId, out var rctx))
        {
            var task = _engine.GetTask(rctx.EngineTaskId);
            return new TransferStatusInfo
            {
                TaskId = rctx.TaskId,
                Role = "receiver",
                PeerDeviceId = rctx.PeerDeviceId,
                FileName = task?.Manifest.FileName ?? "",
                TotalSize = task?.Manifest.TotalSize ?? 0,
                TotalChunks = task?.Manifest.TotalChunks ?? 0,
                ReceivedChunks = task?.ReceivedCount ?? 0,
                Progress = task?.Progress ?? 0,
                Status = task?.Status.ToString().ToLowerInvariant() ?? "active",
                StartedAt = rctx.StartedAt
            };
        }
        return null;
    }

    /// <summary>取消传输任务（通知对方 + 本地清理）。</summary>
    public async Task<bool> CancelTransferAsync(string taskId)
    {
        IPEndPoint? peer = null;
        if (_senders.TryRemove(taskId, out var sctx)) peer = sctx.PeerEndpoint;
        if (_receivers.TryRemove(taskId, out var rctx)) peer ??= rctx.PeerEndpoint;
        _holePunchEndpoints.TryRemove(taskId, out _);
        var keys = _reassembly.Keys.Where(k => k.taskId == taskId).ToList();
        foreach (var k in keys) _reassembly.TryRemove(k, out _);
        if (peer != null)
        {
            try { await SendAsync(peer, EncodeSimple(MsgTransferEnd, taskId)); } catch { }
        }
        return peer != null;
    }

    // ── 帧编解码 ──

    private static byte[] EncodeSimple(byte type, string taskId)
    {
        var idBytes = Encoding.UTF8.GetBytes(taskId);
        var buf = new byte[1 + 2 + idBytes.Length];
        buf[0] = type;
        BitConverter.TryWriteBytes(buf.AsSpan(1, 2), (ushort)idBytes.Length);
        Array.Copy(idBytes, 0, buf, 3, idBytes.Length);
        return buf;
    }

    private static byte[] EncodeChunkRef(byte type, string taskId, int chunkIndex)
    {
        var idBytes = Encoding.UTF8.GetBytes(taskId);
        var buf = new byte[1 + 2 + idBytes.Length + 4];
        buf[0] = type;
        BitConverter.TryWriteBytes(buf.AsSpan(1, 2), (ushort)idBytes.Length);
        Array.Copy(idBytes, 0, buf, 3, idBytes.Length);
        BitConverter.TryWriteBytes(buf.AsSpan(3 + idBytes.Length, 4), chunkIndex);
        return buf;
    }

    private static byte[] EncodeChunkSegment(string taskId, int chunkIndex, int segIndex, int totalSegs, byte[] payload)
    {
        var idBytes = Encoding.UTF8.GetBytes(taskId);
        var buf = new byte[1 + 2 + idBytes.Length + 4 + 4 + 4 + 4 + payload.Length];
        int p = 0;
        buf[p++] = MsgChunkSegment;
        BitConverter.TryWriteBytes(buf.AsSpan(p, 2), (ushort)idBytes.Length); p += 2;
        Array.Copy(idBytes, 0, buf, p, idBytes.Length); p += idBytes.Length;
        BitConverter.TryWriteBytes(buf.AsSpan(p, 4), chunkIndex); p += 4;
        BitConverter.TryWriteBytes(buf.AsSpan(p, 4), segIndex); p += 4;
        BitConverter.TryWriteBytes(buf.AsSpan(p, 4), totalSegs); p += 4;
        BitConverter.TryWriteBytes(buf.AsSpan(p, 4), payload.Length); p += 4;
        Array.Copy(payload, 0, buf, p, payload.Length);
        return buf;
    }

    private static string? ExtractTaskId(byte[] data)
    {
        if (data.Length < 3) return null;
        var idLen = BitConverter.ToUInt16(data, 1);
        if (data.Length < 3 + idLen) return null;
        return Encoding.UTF8.GetString(data, 3, idLen);
    }

    private static (string taskId, int chunkIndex) DecodeChunkRef(byte[] data)
    {
        var taskId = ExtractTaskId(data) ?? "";
        var idLen = BitConverter.ToUInt16(data, 1);
        var chunkIndex = BitConverter.ToInt32(data, 3 + idLen);
        return (taskId, chunkIndex);
    }

    private static ChunkSegmentInfo? DecodeChunkSegment(byte[] data)
    {
        if (data.Length < 3) return null;
        var idLen = BitConverter.ToUInt16(data, 1);
        int p = 3 + idLen;
        if (data.Length < p + 16) return null;
        var chunkIndex = BitConverter.ToInt32(data, p); p += 4;
        var segIndex = BitConverter.ToInt32(data, p); p += 4;
        var totalSegs = BitConverter.ToInt32(data, p); p += 4;
        var payloadLen = BitConverter.ToInt32(data, p); p += 4;
        if (data.Length < p + payloadLen) return null;
        var payload = new byte[payloadLen];
        Array.Copy(data, p, payload, 0, payloadLen);
        var taskId = Encoding.UTF8.GetString(data, 3, idLen);
        return new ChunkSegmentInfo
        {
            TaskId = taskId,
            ChunkIndex = chunkIndex,
            SegmentIndex = segIndex,
            TotalSegments = totalSegs,
            Data = payload
        };
    }

    private async Task SendAsync(IPEndPoint target, byte[] data)
    {
        if (_sendFunc == null)
        {
            _logger.LogWarning("[udp-transfer] 发送函数未绑定");
            return;
        }
        try { await _sendFunc(target, data); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[udp-transfer] 发送失败 to {Target}", target);
        }
    }
}

// ── 辅助类型 ──

internal class SenderContext
{
    public string TaskId { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string PeerDeviceId { get; set; } = "";
    public PieceManifest? Manifest { get; set; }
    public int AckedChunks { get; set; }
    public long SentBytes { get; set; }
    public bool LedgerRecorded { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public IPEndPoint? PeerEndpoint { get; set; }
}

internal class ReceiverContext
{
    public string TaskId { get; set; } = "";
    public string EngineTaskId { get; set; } = "";
    public string PeerDeviceId { get; set; } = "";
    public long ReceivedBytes { get; set; }
    public bool LedgerRecorded { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public IPEndPoint? PeerEndpoint { get; set; }
}

internal class ChunkSegmentInfo
{
    public string TaskId { get; set; } = "";
    public int ChunkIndex { get; set; }
    public int SegmentIndex { get; set; }
    public int TotalSegments { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

/// <summary>chunk 重组缓冲区：收集所有 segment 后组装回完整 chunk。</summary>
internal class SegmentReassembly
{
    private readonly byte[][] _segments;
    private int _received;

    public SegmentReassembly(int totalSegments)
    {
        _segments = new byte[totalSegments][];
        _received = 0;
    }

    public bool SetSegment(int index, byte[] data)
    {
        if (index < 0 || index >= _segments.Length) return false;
        if (_segments[index] != null) return false; // 重复 segment
        _segments[index] = data;
        _received++;
        return true;
    }

    public bool IsComplete => _received == _segments.Length;

    public byte[] Assemble()
    {
        int total = 0;
        foreach (var s in _segments) total += s?.Length ?? 0;
        var result = new byte[total];
        int offset = 0;
        foreach (var s in _segments)
        {
            if (s == null) continue;
            Array.Copy(s, 0, result, offset, s.Length);
            offset += s.Length;
        }
        return result;
    }
}

/// <summary>传输任务状态快照（供 REST API 返回前端）。</summary>
public class TransferStatusInfo
{
    public string TaskId { get; set; } = "";
    public string Role { get; set; } = "";          // "sender" | "receiver"
    public string PeerDeviceId { get; set; } = "";
    public string FileName { get; set; } = "";
    public long TotalSize { get; set; }
    public int TotalChunks { get; set; }
    public int ReceivedChunks { get; set; }
    public double Progress { get; set; }
    public string Status { get; set; } = "";         // "active" | "complete" | "failed"
    public DateTime StartedAt { get; set; }
}
