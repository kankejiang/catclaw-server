using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CatClawMusicServer.ClawCircle.Ledger;

namespace CatClawMusicServer.ClawCircle;

/// <summary>
/// 猫爪驿站 WebSocket 信令中间件：拦截 <see cref="ClawCircleProtocol.Path"/> 路径，
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

    public async Task InvokeAsync(HttpContext context, ClawCircleTracker tracker, ServerAuthOptions auth,
        BlockchainLedger ledger, CatClawMusicServer.ClawCircle.Accounts.AccountService accountService)
    {
        var path = context.Request.Path.Value ?? "";

        // 仅处理猫爪驿站信令路径，其余请求交给后续管道
        if (!path.Equals(ClawCircleProtocol.Path, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // 订阅账本新区块事件（每个 WebSocket 连接独立订阅，断开时自动 GC）
        Func<Block, Task> onBlock = block =>
            tracker.BroadcastAsync(new NewBlockMsg { Block = block }, ct: context.RequestAborted);
        ledger.OnBlockMined += onBlock;

        // ── 鉴权（二选一）──
        // 优先用 clawToken（账号 Token），其次用服务端 AccessToken
        long? accountId = null;
        var clawToken = context.Request.Query["clawToken"].ToString() ?? "";
        if (!string.IsNullOrEmpty(clawToken))
        {
            var account = await accountService.ValidateTokenAsync(clawToken);
            if (account == null)
            {
                context.Response.StatusCode = 401;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync("Invalid claw token");
                return;
            }
            accountId = account.Id;
        }
        else if (!string.IsNullOrEmpty(auth.AccessToken))
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
        try
        {
            await HandleSocketAsync(context, socket, tracker, ledger, accountService, accountId);
        }
        finally
        {
            // 退订账本事件，避免内存泄漏
            ledger.OnBlockMined -= onBlock;
        }
    }

    private async Task HandleSocketAsync(HttpContext context, WebSocket socket, ClawCircleTracker tracker,
        BlockchainLedger ledger, CatClawMusicServer.ClawCircle.Accounts.AccountService accountService, long? accountId)
    {
        var deviceId = "";
        var ct = context.RequestAborted;
        var buffer = new byte[8192];
        var ms = new MemoryStream();
        var shouldClose = false;
        const int MaxMessageSize = 256 * 1024; // 256KB 上限，防止恶意大包占满内存

        // 简单速率限制：每秒最多 10 条消息
        var msgTimestamps = new List<DateTime>();
        const int MaxMsgPerSecond = 10;

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

                    // 消息大小限制
                    if (ms.Length > MaxMessageSize)
                    {
                        await SendSocketJsonAsync(socket, new ErrorMsg { ErrorText = "message too large" });
                        shouldClose = true;
                        break;
                    }
                } while (!result.EndOfMessage);

                if (shouldClose) break;

                // 速率限制检查
                var now = DateTime.UtcNow;
                msgTimestamps.RemoveAll(t => (now - t).TotalSeconds >= 1);
                if (msgTimestamps.Count >= MaxMsgPerSecond)
                {
                    await SendSocketJsonAsync(socket, new ErrorMsg { ErrorText = "rate limit exceeded" });
                    continue;
                }
                msgTimestamps.Add(now);

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
                            deviceId = await HandleRegisterAsync(doc.RootElement, socket, tracker, context, ledger, accountService, accountId);
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

    private async Task<string> HandleRegisterAsync(JsonElement root, WebSocket socket, ClawCircleTracker tracker, HttpContext context, BlockchainLedger ledger,
        CatClawMusicServer.ClawCircle.Accounts.AccountService accountService, long? accountId)
    {
        var msg = root.Deserialize<RegisterMsg>(ClawCircleJson.Options) ?? new RegisterMsg();

        var deviceId = msg.DeviceId;
        if (string.IsNullOrWhiteSpace(deviceId))
            deviceId = context.Request.Query["deviceId"].ToString() ?? "";
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            // 直接通过 socket 发送错误（此时节点尚未注册，tracker.SendToAsync 找不到目标）
            await SendSocketJsonAsync(socket, new ErrorMsg { ErrorText = "register requires deviceId" });
            return "";
        }

        var name = string.IsNullOrWhiteSpace(msg.Name)
            ? context.Request.Query["name"].ToString() ?? ""
            : msg.Name;
        if (string.IsNullOrWhiteSpace(name))
            name = deviceId;

        // Wan/Port 保存给「UDP 反射端点」使用（由 STUN UDP 服务在节点打 STUN 包时写入）。
        // 此处注册时先用客户端自报值（通常为空），STUN 到达后由 tracker.SetUdpEndpoint 覆盖。
        // 记录 WebSocket 源 IP 供 STUN 服务做源 IP 绑定校验。
        var wsIp = context.Connection.RemoteIpAddress;
        var info = tracker.Register(deviceId, name, socket, msg.Wan, msg.Port, msg.RelayOnly, msg.Library, wsIp);

        // 关联 deviceId 到账号（若通过 clawToken 鉴权），积分将记录在账号维度
        if (accountId.HasValue)
        {
            ledger.BindDeviceToAccount(deviceId, accountId.Value);
            Console.WriteLine($"[clawcircle] 设备 {deviceId} 关联到账号 #{accountId.Value}");
        }
        else
        {
            // 旧式无账号的设备，按 deviceId 记账
            ledger.RegisterNode(deviceId);
        }

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

        Console.WriteLine($"[clawcircle] 节点上线: {name} ({deviceId}), 账号 {(accountId.HasValue ? "#" + accountId.Value : "未绑定")}, 当前在线 {tracker.OnlineCount}");
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

    /// <summary>直接通过 socket 发送 JSON 消息（用于节点尚未注册时的错误反馈）。</summary>
    private static async Task SendSocketJsonAsync(WebSocket socket, object message)
    {
        if (socket.State != WebSocketState.Open) return;
        try
        {
            var json = JsonSerializer.Serialize(message, ClawCircleJson.Options);
            var bytes = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        }
        catch
        {
            // 忽略发送阶段的异常
        }
    }
}
