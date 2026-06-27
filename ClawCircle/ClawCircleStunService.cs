using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CatClawMusicServer.ClawCircle;

/// <summary>
/// 猫爪圈 STUN 服务：在 UDP 端口（默认 37824）监听节点打来的「反射端点探测包」，
/// 观察其 UDP 源地址（NAT 映射后的公网 IP:端口），写入 tracker 的 PeerInfo.Wan/Port，
/// 并回包告知节点自身反射端点。这是 NAT 打洞直连的前提——只有服务端能观察到节点的 UDP 反射端点。
/// </summary>
public class ClawCircleStunService
{
    private readonly ClawCircleTracker _tracker;
    private int _port;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;

    public ClawCircleStunService(ClawCircleTracker tracker, int port)
    {
        _tracker = tracker;
        _port = port;
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
        _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        Console.WriteLine($"[clawcircle-stun] 监听 UDP :{_port}");
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _udp?.Close(); } catch { }
        try { _udp?.Dispose(); } catch { }
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
                var ip = remote.Address.ToString();
                var port = remote.Port;

                string? deviceId = null;
                try
                {
                    var text = Encoding.UTF8.GetString(result.Buffer);
                    using var doc = JsonDocument.Parse(text);
                    deviceId = doc.RootElement.TryGetProperty("deviceId", out var d) ? d.GetString() : null;
                }
                catch { }

                if (!string.IsNullOrEmpty(deviceId))
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
