using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace CatClawMusicServer.ClawCircle;

/// <summary>
/// 猫爪驿站 tracker 内存在线节点注册表（单例）。
/// 线程安全：ConcurrentDictionary 管理节点，每个 WebSocket 发送用独立 SemaphoreSlim 串行，避免并发写帧竞争。
/// </summary>
public class ClawCircleTracker
{
    private readonly ConcurrentDictionary<string, PeerSession> _peers = new();
    private readonly SemaphoreSlim _global = new(1, 1);

    public int OnlineCount => _peers.Count;

    /// <summary>当前在线节点快照（用于 welcome / REST 调试）。</summary>
    public IReadOnlyList<PeerInfo> Snapshot()
    {
        var list = new List<PeerInfo>();
        foreach (var s in _peers.Values)
            list.Add(s.Info);
        return list;
    }

    /// <summary>在线节点 ID + 连接时间（供账本在线奖励使用）。</summary>
    public List<(string deviceId, DateTime connectedAt)> GetOnlineNodes()
    {
        var list = new List<(string, DateTime)>();
        foreach (var s in _peers.Values)
            list.Add((s.DeviceId, s.Info.ConnectedAt));
        return list;
    }

    public PeerInfo? Find(string deviceId)
        => _peers.TryGetValue(deviceId, out var s) ? s.Info : null;

    /// <summary>注册或更新一个节点（首次上线 / library 变化时调用）。</summary>
    public PeerInfo Register(string deviceId, string name, WebSocket socket,
        string? wan = null, int? port = null, bool relayOnly = false, LibrarySummary? library = null,
        System.Net.IPAddress? wsIpAddress = null)
    {
        var info = new PeerInfo
        {
            DeviceId = deviceId,
            Name = name,
            Wan = wan,
            Port = port,
            RelayOnly = relayOnly,
            Library = library,
            ConnectedAt = DateTime.UtcNow,
            WsIpAddress = wsIpAddress
        };
        var session = new PeerSession
        {
            DeviceId = deviceId,
            Name = name,
            Socket = socket,
            Info = info
        };
        _peers[deviceId] = session;
        return info;
    }

    /// <summary>更新某节点曲库摘要（library_update 时）。</summary>
    public bool UpdateLibrary(string deviceId, LibrarySummary library)
    {
        if (!_peers.TryGetValue(deviceId, out var s))
            return false;
        s.Info.Library = library;
        return true;
    }

    /// <summary>设置某节点的 UDP 反射端点（STUN 服务观察到后写入，供 NAT 打洞直连）。</summary>
    public bool SetUdpEndpoint(string deviceId, string wan, int port)
    {
        if (!_peers.TryGetValue(deviceId, out var s))
            return false;
        s.Info.Wan = wan;
        s.Info.Port = port;
        return true;
    }

    /// <summary>注销节点，返回被移除的快照（用于广播 offline）。</summary>
    public PeerInfo? Remove(string deviceId)
    {
        if (_peers.TryRemove(deviceId, out var s))
            return s.Info;
        return null;
    }

    /// <summary>给指定节点发消息（线程安全）。目标不在线返回 false。</summary>
    public async Task<bool> SendToAsync(string deviceId, object message, CancellationToken ct = default)
    {
        if (!_peers.TryGetValue(deviceId, out var s))
            return false;
        await SendAsync(s, message, ct);
        return true;
    }

    /// <summary>广播给所有节点（exceptDeviceId 可排除发送者）。</summary>
    public async Task BroadcastAsync(object message, string? exceptDeviceId = null, CancellationToken ct = default)
    {
        foreach (var s in _peers.Values)
        {
            if (s.DeviceId == exceptDeviceId) continue;
            try { await SendAsync(s, message, ct); }
            catch { /* 单个节点发送失败不影响其他 */ }
        }
    }

    private static async Task SendAsync(PeerSession session, object message, CancellationToken ct)
    {
        var sock = session.Socket;
        if (sock == null || sock.State != WebSocketState.Open)
            return;
        // 每个 socket 串行发送，避免两帧交错
        await session.SendLock.WaitAsync(ct);
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(message, ClawCircleJson.Options);
            await sock.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            session.SendLock.Release();
        }
    }
}

/// <summary>单个在线节点的运行时会话（含 WebSocket 与发送锁）。</summary>
public class PeerSession
{
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public WebSocket? Socket { get; set; }
    public PeerInfo Info { get; set; } = new();
    public SemaphoreSlim SendLock { get; } = new(1, 1);
}

/// <summary>ClawCircle 统一的 JSON 序列化选项（camelCase，大小写不敏感）。</summary>
public static class ClawCircleJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
