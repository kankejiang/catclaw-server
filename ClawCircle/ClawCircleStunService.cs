using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CatClawMusicServer.ClawCircle.Transfer;

namespace CatClawMusicServer.ClawCircle;

/// <summary>
/// 猫爪驿站 UDP 服务：在单个 UDP 端口（默认 37824）上多路复用两类协议——
///   1. STUN 反射端点探测（JSON 包，首字节 '{'=0x7B）
///   2. P2P 数据传输（二进制帧，首字节 0x01-0x08，由 <see cref="UdpTransferProtocol"/> 处理）
///
/// 端口共享是 NAT 打洞的硬性要求：STUN 观察到的反射端点必须就是后续 P2P 传输用的端点。
/// 若分开端口，对方打洞发到的端点没有传输 socket 监听，传输无法建立。
///
/// 安全（STUN 部分）：仅当 deviceId 已在 tracker 注册且源 IP 与 WebSocket 连接 IP 一致
/// 时才更新反射端点，防止攻击者伪造 deviceId 劫持节点。
/// </summary>
public class ClawCircleStunService
{
    private readonly ClawCircleTracker _tracker;
    private readonly UdpTransferProtocol? _transfer;
    private int _port;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;

    public ClawCircleStunService(ClawCircleTracker tracker, int port, UdpTransferProtocol? transfer = null)
    {
        _tracker = tracker;
        _port = port;
        _transfer = transfer;
    }

    public void Start()
    {
        // IPv6 双栈优先（可同时收 IPv4 映射包），失败退纯 IPv4
        if (!TryBind(IPAddress.IPv6Any, true, out _udp, ref _port)
            && !TryBind(IPAddress.Any, false, out _udp, ref _port))
        {
            Console.WriteLine($"[clawcircle-stun] UDP 端口 {_port} 绑定失败，STUN 不可用");
            return;
        }
        _cts = new CancellationTokenSource();
        // 将 UDP 发送能力注入传输协议（共享同一个 socket，保证反射端点一致）
        _transfer?.BindSendFunc(SendAsync);
        _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        Console.WriteLine($"[clawcircle-stun] 监听 UDP :{_port}（STUN + P2P 传输多路复用）");
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _udp?.Close(); } catch { }
        try { _udp?.Dispose(); } catch { }
    }

    /// <summary>通过共享的 UDP socket 发送数据包（供 UdpTransferProtocol 复用）。</summary>
    public async Task SendAsync(IPEndPoint target, byte[] data)
    {
        var udp = _udp;
        if (udp == null) return;
        try { await udp.SendAsync(data, data.Length, target); }
        catch { /* 发送失败由调用方处理 */ }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var udp = _udp;
        if (udp == null) return;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try { result = await udp.ReceiveAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch { continue; }

                var remote = result.RemoteEndPoint;

                // 多路复用：二进制传输包（首字节 0x01-0x08）交给传输协议处理；
                // STUN JSON 包首字节是 '{' (0x7B)，走下方 STUN 路径。
                if (_transfer != null && result.Buffer.Length > 0)
                {
                    var first = result.Buffer[0];
                    if (first >= 0x01 && first <= 0x08)
                    {
                        try { await _transfer.HandlePacketAsync(result.Buffer, remote); } catch { }
                        continue;
                    }
                }

                var ip = remote.Address.ToString();
                var port = remote.Port;

                string? deviceId = null;
                try
                {
                    var text = Encoding.UTF8.GetString(result.Buffer);
                    using var doc = JsonDocument.Parse(text);
                    deviceId = doc.RootElement.TryGetProperty("deviceId", out var d) ? d.GetString() : null;
                }
                catch { /* 无效 JSON，忽略 */ }

                if (string.IsNullOrEmpty(deviceId)) continue;

                // 安全校验：仅允许已注册节点更新自己的反射端点，
                // 且源 IP 必须与 WebSocket 注册时记录的 IP 一致，防止 deviceId 劫持。
                var peer = _tracker.Find(deviceId);
                if (peer == null) continue; // 未注册节点忽略

                // IPv4/IPv6 混合比较：去掉 IPv6 映射前缀
                var remoteIp = remote.Address.MapToIPv4().ToString();
                var peerIp = peer.WsIpAddress?.MapToIPv4().ToString();
                if (peerIp != null && peerIp != remoteIp && peerIp != "0.0.0.0")
                {
                    // IP 不匹配，拒绝更新（可能攻击者伪造 deviceId）
                    Console.WriteLine($"[clawcircle-stun] 拒绝更新 {deviceId} 反射端点：源 IP 不匹配 (peer={peerIp}, stun={remoteIp})");
                    continue;
                }

                _tracker.SetUdpEndpoint(deviceId, ip, port);

                // 回包：告知节点自身反射端点
                var reply = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { wan = ip, port }));
                try { await udp.SendAsync(reply, reply.Length, remote); } catch { }
            }
        }
        catch (OperationCanceledException) { }
    }

    private static bool TryBind(IPAddress addr, bool dualMode, out UdpClient? udp, ref int port)
    {
        udp = null;
        var startPort = port;
        for (int i = 0; i < 20; i++)
        {
            try
            {
                var u = new UdpClient(addr.AddressFamily);
                u.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                if (dualMode) u.Client.DualMode = true;
                u.Client.Bind(new IPEndPoint(addr, port));
                udp = u;
                return true;
            }
            catch
            {
                port++;
                udp?.Dispose();
                udp = null;
            }
        }
        port = startPort;
        return false;
    }
}
