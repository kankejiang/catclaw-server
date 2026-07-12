using System.Net.WebSockets;
using System.Text;
using CatClawMusicServer.Services;

namespace CatClawMusicServer;

/// <summary>
/// WebSocket 事件中间件 — 拦截 /ws/events，升级为 WebSocket 并注册到 EventBus。
/// 客户端连接后自动接收全局事件推送，断开时自动注销。
/// </summary>
public class EventWebSocketMiddleware
{
    private readonly RequestDelegate _next;

    public EventWebSocketMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, EventBus eventBus)
    {
        if (!context.Request.Path.Equals("/ws/events", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            return;
        }

        var socket = await context.WebSockets.AcceptWebSocketAsync();
        var connectionId = Guid.NewGuid().ToString("N");

        eventBus.Subscribe(connectionId, socket);

        try
        {
            // 发送欢迎消息
            var welcomeBytes = Encoding.UTF8.GetBytes(
                $"{{\"type\":\"connected\",\"data\":{{\"connection_id\":\"{connectionId}\"}}}}");
            await socket.SendAsync(new ArraySegment<byte>(welcomeBytes),
                WebSocketMessageType.Text, true, CancellationToken.None);

            // 保持连接直到客户端断开
            var buffer = new byte[1024];
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing",
                        CancellationToken.None);
                    break;
                }

                // 客户端可以发送 ping，回复 pong
                if (result.Count > 0)
                {
                    var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    if (msg.Contains("ping"))
                    {
                        var pong = Encoding.UTF8.GetBytes("{\"type\":\"pong\"}");
                        await socket.SendAsync(new ArraySegment<byte>(pong),
                            WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
            }
        }
        catch (WebSocketException)
        {
            // 客户端异常断开
        }
        finally
        {
            eventBus.Unsubscribe(connectionId);
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done",
                        CancellationToken.None);
                }
                catch { }
            }
        }
    }
}
