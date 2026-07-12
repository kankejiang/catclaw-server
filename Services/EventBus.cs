using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CatClawMusicServer.Services;

/// <summary>
/// 全局事件总线 — 服务层发布事件，WebSocket 中间件订阅并推送给客户端。
/// </summary>
public class EventBus
{
    private readonly ConcurrentDictionary<string, WebSocket> _subscribers = new();
    private readonly ILogger<EventBus> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public EventBus(ILogger<EventBus> logger) => _logger = logger;

    /// <summary>注册一个 WebSocket 订阅者</summary>
    public void Subscribe(string connectionId, WebSocket socket)
    {
        _subscribers[connectionId] = socket;
        _logger.LogDebug("WebSocket 订阅者上线: {Id} (共 {Count})", connectionId, _subscribers.Count);
    }

    /// <summary>移除订阅者</summary>
    public void Unsubscribe(string connectionId)
    {
        _subscribers.TryRemove(connectionId, out _);
        _logger.LogDebug("WebSocket 订阅者下线: {Id} (共 {Count})", connectionId, _subscribers.Count);
    }

    /// <summary>发布事件给所有已连接的订阅者</summary>
    public async Task PublishAsync(string eventType, object? data = null)
    {
        if (_subscribers.IsEmpty) return;

        var message = new EventMessage
        {
            Type = eventType,
            Data = data,
            Timestamp = DateTime.UtcNow
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOpts);
        var segment = new ArraySegment<byte>(bytes);

        var deadConnections = new List<string>();

        foreach (var (id, socket) in _subscribers)
        {
            if (socket.State != WebSocketState.Open)
            {
                deadConnections.Add(id);
                continue;
            }

            try
            {
                await socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WebSocket 发送失败: {Id}", id);
                deadConnections.Add(id);
            }
        }

        // 清理已断开的连接
        foreach (var id in deadConnections)
            _subscribers.TryRemove(id, out _);
    }

    /// <summary>当前在线订阅者数量</summary>
    public int SubscriberCount => _subscribers.Count;
}

public class EventMessage
{
    public string Type { get; set; } = "";
    public object? Data { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>事件类型常量</summary>
public static class EventTypes
{
    public const string NowPlayingChanged = "now_playing_changed";
    public const string PlayQueueUpdated = "play_queue_updated";
    public const string ScanProgress = "scan_progress";
    public const string LibraryChanged = "library_changed";
    public const string FavoriteChanged = "favorite_changed";
    public const string RatingChanged = "rating_changed";
}
