using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CatClawMusicServer.ClawCircle;

/// <summary>
/// 猫爪圈 WebSocket 信令中间件：拦截 <see cref="ClawCircleProtocol.Path"/> 路径，
/// 完成 token 鉴权、AcceptWebSocket，并分发 register / library_update / query_peer /
/// find_song / signal / bye 等消息。不匹配该路径时直接短路到下一个中间件。
///
/// 鉴权：与 ApiAuthMiddleware 共用同一个 AccessToken —— 支持 URL 查询参数
/// <c>?token=</c> 或 <c>Authorization: Bearer</c> 头。未配置 AccessToken 时免鉴权（本地开发）。
/// </summary>
public class ClawCircleWebSocketMiddleware
{
    private readonly RequestDelegate _next;

    public ClawCircleWebSocketMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ClawCircleTracker tracker, ServerAuthOptions auth)
    {
        var path = context.Request.Path.Value ?? "";

        // 仅处理猫爪圈信令路径，其余请求交给后续管道
        if (!path.Equals(ClawCircleProtocol.Path, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // ── 鉴权 ──
        if (!string.IsNullOrEmpty(auth.AccessToken))
        {
            var tok = context.Request.Query["token"].ToString() ?? "";
            if (string.IsNullOrEmpty(tok))
            {
                var authHeader = context.Request.Headers["Authorization"].ToString();
                if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    tok = authHeader["Bearer ".Length..].Trim();
            }
            if (tok != auth.AccessToken)
            {
                context.Response.StatusCode = 401;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync("Unauthorized");
                return;
            }
        }

        // ── 升级为 WebSocket ──
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 426; // Upgrade Required
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("Upgrade Required: use WebSocket");
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await HandleSocketAsync(context, socket, tracker);
    }

    private async Task HandleSocketAsync(HttpContext context, WebSocket socket, ClawCircleTracker tracker)
    {
        var deviceId = "";
        var ct = context.RequestAborted;
        var buffer = new byte[8192];
        var ms = new MemoryStream();
        var shouldClose = false;

        try
        {
            WebSocketReceiveResult result;
            while (!shouldClose)
            {
                ms.SetLength(0);
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        shouldClose = true;
                        break;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (shouldClose) break;

                ms.Position = 0;
                JsonDocument? doc = null;
                try
                {
                    doc = await JsonDocument.ParseAsync(ms, cancellationToken: ct);
                    var type = doc.RootElement.TryGetProperty("type", out var tEl)
                        ? tEl.GetString() ?? ""
                        : "";

                    switch (type)
                    {
                        case ClawCircleProtocol.Register:
                            deviceId = await HandleRegisterAsync(doc.RootElement, socket, tracker, context);
                            break;

                        case ClawCircleProtocol.LibraryUpdate:
                            if (!string.IsNullOrEmpty(deviceId))
                                await HandleLibraryUpdateAsync(doc.RootElement, tracker, deviceId);
                            break;

                        case ClawCircleProtocol.QueryPeer:
                            if (!string.IsNullOrEmpty(deviceId))
                                await HandleQueryPeerAsync(doc.RootElement, tracker, deviceId);
                            break;

                        case ClawCircleProtocol.FindSong:
                            if (!string.IsNullOrEmpty(deviceId))
                                await HandleFindSongAsync(doc.RootElement, tracker, deviceId);
                            break;

                        case ClawCircleProtocol.Signal:
                            if (!string.IsNullOrEmpty(deviceId))
                                await HandleSignalAsync(doc.RootElement, tracker, deviceId);
                            break;

                        case ClawCircleProtocol.Bye:
                            shouldClose = true;
                            break;

                        default:
                            if (!string.IsNullOrEmpty(deviceId))
                                await tracker.SendToAsync(deviceId,
                                    new ErrorMsg { ErrorText = $"unknown message type: {type}" }, ct);
                            break;
                    }
                }
                finally
                {
                    doc?.Dispose();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 客户端断开 / 请求中止
        }
        catch (WebSocketException)
        {
            // 连接异常断开
        }
        catch (Exception ex)
        {
            // 解析或业务异常：记录日志但不影响整体服务
            Console.WriteLine($"[clawcircle] 处理消息异常 deviceId={deviceId}: {ex.Message}");
        }

        await CleanupAsync(socket, tracker, deviceId, ct);
    }

    // ── 各消息处理 ──

    private async Task<string> HandleRegisterAsync(JsonElement root, WebSocket socket, ClawCircleTracker tracker, HttpContext context)
    {
        var msg = root.Deserialize<RegisterMsg>(ClawCircleJson.Options) ?? new RegisterMsg();

        var deviceId = msg.DeviceId;
        if (string.IsNullOrWhiteSpace(deviceId))
            deviceId = context.Request.Query["deviceId"].ToString() ?? "";
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            await tracker.SendToAsync(deviceId, new ErrorMsg { ErrorText = "register requires deviceId" });
            return "";
        }

        var name = string.IsNullOrWhiteSpace(msg.Name)
            ? context.Request.Query["name"].ToString() ?? ""
            : msg.Name;
        if (string.IsNullOrWhiteSpace(name))
            name = deviceId;

        // Wan/Port 保存给「UDP 反射端点」使用（由 STUN UDP 服务在节点打 STUN 包时写入）。
        // 此处注册时先用客户端自报值（通常为空），STUN 到达后由 tracker.SetUdpEndpoint 覆盖。
        var info = tracker.Register(deviceId, name, socket, msg.Wan, msg.Port, msg.RelayOnly, msg.Library);

        // 回 welcome（排除自己）
        var welcome = new WelcomeMsg
        {
            You = deviceId,
            Peers = tracker.Snapshot().Where(p => p.DeviceId != deviceId).ToList(),
            ServerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await tracker.SendToAsync(deviceId, welcome, CancellationToken.None);

        // 通知其他节点上线
        await tracker.BroadcastAsync(new PeerOnlineMsg { Peer = info }, exceptDeviceId: deviceId,
            ct: CancellationToken.None);

        Console.WriteLine($"[clawcircle] 节点上线: {name} ({deviceId}), 当前在线 {tracker.OnlineCount}");
        return deviceId;
    }

    private async Task HandleLibraryUpdateAsync(JsonElement root, ClawCircleTracker tracker, string deviceId)
    {
        var msg = root.Deserialize<LibraryUpdateMsg>(ClawCircleJson.Options);
        if (msg == null || msg.Library == null) return;
        if (tracker.UpdateLibrary(deviceId, msg.Library))
            await tracker.BroadcastAsync(new PeerUpdateMsg { DeviceId = deviceId, Library = msg.Library },
                exceptDeviceId: deviceId, ct: CancellationToken.None);
    }

    private async Task HandleQueryPeerAsync(JsonElement root, ClawCircleTracker tracker, string deviceId)
    {
        var msg = root.Deserialize<QueryPeerMsg>(ClawCircleJson.Options);
        if (msg == null) return;
        var peer = tracker.Find(msg.DeviceId);
        if (peer != null)
            await tracker.SendToAsync(deviceId, new PeerInfoMsg { Peer = peer });
        else
            await tracker.SendToAsync(deviceId, new ErrorMsg { ErrorText = $"peer not found: {msg.DeviceId}" });
    }

    private async Task HandleFindSongAsync(JsonElement root, ClawCircleTracker tracker, string deviceId)
    {
        var msg = root.Deserialize<FindSongMsg>(ClawCircleJson.Options);
        if (msg == null) return;
        var key = (msg.SongKey ?? "").ToLowerInvariant();
        var holders = tracker.Snapshot()
            .Where(p => p.Library != null && p.Library.SongKeys.Any(k => k.ToLowerInvariant() == key))
            .ToList();
        await tracker.SendToAsync(deviceId,
            new SongHoldersMsg { SongKey = msg.SongKey ?? "", Holders = holders });
    }

    private async Task HandleSignalAsync(JsonElement root, ClawCircleTracker tracker, string deviceId)
    {
        var msg = root.Deserialize<SignalMsg>(ClawCircleJson.Options);
        if (msg == null) return;
        if (tracker.Find(msg.To) != null)
        {
            await tracker.SendToAsync(msg.To, new RelayMsg { From = deviceId, Data = msg.Data });
        }
        else
        {
            await tracker.SendToAsync(deviceId, new ErrorMsg { ErrorText = $"target not online: {msg.To}" });
        }
    }

    private async Task CleanupAsync(WebSocket socket, ClawCircleTracker tracker, string deviceId, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(deviceId))
        {
            var removed = tracker.Remove(deviceId);
            if (removed != null)
            {
                await tracker.BroadcastAsync(new PeerOfflineMsg { DeviceId = deviceId },
                    ct: CancellationToken.None);
                Console.WriteLine($"[clawcircle] 节点下线: {removed.Name} ({deviceId}), 当前在线 {tracker.OnlineCount}");
            }
        }
        try
        {
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", ct);
        }
        catch
        {
            // 忽略关闭阶段的异常
        }
    }
}
