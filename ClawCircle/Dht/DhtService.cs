using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CatClawMusicServer.ClawCircle.Dht;

/// <summary>
/// Kademlia DHT 服务 — UDP 节点发现 + 键值存储。
/// 实现 PING / FIND_NODE / FIND_VALUE / STORE 四种 RPC。
/// </summary>
public class DhtService : IDisposable
{
    private readonly DhtOptions _opts;
    private readonly RoutingTable _routing;
    private readonly ConcurrentKeyValueStore _store;
    private readonly ILogger<DhtService> _logger;

    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<DhtRpcResponse>> _pendingRpcs = new();
    private readonly Random _rand = new();

    public NodeId LocalId => _routing.LocalId;
    public int NodeCount => _routing.TotalNodes;
    public int StoreCount => _store.Count;

    /// <summary>获取路由表中所有已知节点</summary>
    public List<DhtNode> GetAllNodes() => _routing.GetAllNodes();

    public DhtService(DhtOptions opts, ILogger<DhtService> logger, ILogger<RoutingTable> rtLogger)
    {
        _opts = opts;
        _logger = logger;
        _store = new ConcurrentKeyValueStore();
        _routing = new RoutingTable(NodeId.FromString(opts.NodeIdSeed), rtLogger);
    }

    /// <summary>启动 DHT UDP 监听</summary>
    public void Start()
    {
        _cts = new CancellationTokenSource();
        _udp = new UdpClient(_opts.Port, AddressFamily.InterNetwork);
        _logger.LogInformation("DHT 服务启动: port={Port}, id={Id}", _opts.Port, _routing.LocalId);

        _ = ReceiveLoopAsync(_cts.Token);
        _ = MaintenanceLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _udp?.Close();
        _logger.LogInformation("DHT 服务已停止");
    }

    /// <summary>从 Bootstrap 节点加入网络（支持域名和 IP）</summary>
    public async Task BootstrapAsync(IPEndPoint bootstrap)
    {
        _logger.LogInformation("DHT Bootstrap: connecting to {EP}", bootstrap);
        var resp = await SendRpcAsync(bootstrap, new DhtRpcRequest
        {
            Type = DhtRpcType.Ping,
            SenderId = _routing.LocalId.ToString(),
            Id = NewRpcId()
        });

        if (resp != null)
        {
            _routing.AddOrUpdate(new DhtNode
            {
                Id = NodeId.FromHex(resp.SenderId),
                Endpoint = bootstrap
            });
            _logger.LogInformation("Bootstrap 成功，开始 FIND_NODE 查找自身");

            // 查找距离自己最近的节点（填充路由表）
            await IterativeFindNodeAsync(_routing.LocalId);
        }
    }

    /// <summary>从字符串地址 Bootstrap（支持 host:port 域名解析）</summary>
    public async Task<bool> BootstrapFromAddressAsync(string address)
    {
        if (string.IsNullOrEmpty(address) || !address.Contains(':'))
            return false;

        var lastColon = address.LastIndexOf(':');
        var host = address[..lastColon];
        if (!int.TryParse(address[(lastColon + 1)..], out var port))
            return false;

        try
        {
            var addresses = await System.Net.Dns.GetHostAddressesAsync(host);
            if (addresses.Length == 0) return false;

            var ep = new IPEndPoint(addresses[0], port);
            await BootstrapAsync(ep);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DHT Bootstrap DNS 解析失败: {Host}", host);
            return false;
        }
    }

    /// <summary>迭代查找距离目标最近的 K 个节点</summary>
    public async Task<List<DhtNode>> IterativeFindNodeAsync(NodeId target)
    {
        var closest = _routing.FindClosest(target, RoutingTable.Alpha);
        if (closest.Count == 0) return new List<DhtNode>();

        var queried = new HashSet<string>();
        var result = new List<DhtNode>(closest);

        foreach (var node in closest)
        {
            if (queried.Contains(node.Id.ToString())) continue;
            queried.Add(node.Id.ToString());

            var resp = await SendRpcAsync(node.Endpoint, new DhtRpcRequest
            {
                Type = DhtRpcType.FindNode,
                SenderId = _routing.LocalId.ToString(),
                Target = target.ToString(),
                Id = NewRpcId()
            });

            if (resp?.Nodes != null)
            {
                foreach (var n in resp.Nodes)
                {
                    var nodeId = NodeId.FromHex(n.Id);
                    var ep = new IPEndPoint(IPAddress.Parse(n.Address), n.Port);
                    var dhtNode = new DhtNode { Id = nodeId, Endpoint = ep };
                    _routing.AddOrUpdate(dhtNode);
                    result.Add(dhtNode);
                }
            }
        }

        // 按距离排序，返回最近的 K 个
        return result
            .OrderBy(n => target.XorDistance(n.Id))
            .Take(RoutingTable.K)
            .ToList();
    }

    /// <summary>存储键值对到网络</summary>
    public async Task StoreAsync(string key, string value)
    {
        var keyId = NodeId.FromString(key);

        // 本地存储
        _store.Put(key, value);

        // 查找距离 key 最近的节点
        var closest = await IterativeFindNodeAsync(keyId);

        // 向最近的节点发送 STORE
        foreach (var node in closest.Take(RoutingTable.Alpha))
        {
            await SendRpcAsync(node.Endpoint, new DhtRpcRequest
            {
                Type = DhtRpcType.Store,
                SenderId = _routing.LocalId.ToString(),
                Key = key,
                Value = value,
                Id = NewRpcId()
            });
        }

        _logger.LogDebug("DHT STORE: key={Key} stored to {Count} nodes", key, Math.Min(closest.Count, RoutingTable.Alpha));
    }

    /// <summary>从网络查找值</summary>
    public async Task<string?> FindValueAsync(string key)
    {
        // 先查本地
        var local = _store.Get(key);
        if (local != null) return local;

        var keyId = NodeId.FromString(key);
        var closest = await IterativeFindNodeAsync(keyId);

        foreach (var node in closest)
        {
            var resp = await SendRpcAsync(node.Endpoint, new DhtRpcRequest
            {
                Type = DhtRpcType.FindValue,
                SenderId = _routing.LocalId.ToString(),
                Key = key,
                Id = NewRpcId()
            });

            if (resp?.Value != null) return resp.Value;
        }

        return null;
    }

    // ── 内部实现 ──

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udp!.ReceiveAsync(ct);
                var json = Encoding.UTF8.GetString(result.Buffer);
                _ = Task.Run(() => HandleMessageAsync(json, result.RemoteEndPoint, ct), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DHT 接收错误");
            }
        }
    }

    private async Task HandleMessageAsync(string json, IPEndPoint sender, CancellationToken ct)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("rpcId", out var rpcIdEl))
            {
                // 这是 RPC 响应
                var rpcId = rpcIdEl.GetString()!;
                if (_pendingRpcs.TryRemove(rpcId, out var tcs))
                {
                    var resp = JsonSerializer.Deserialize<DhtRpcResponse>(json, _jsonOpts);
                    if (resp != null) tcs.SetResult(resp);
                }
                return;
            }

            // 这是 RPC 请求
            var req = JsonSerializer.Deserialize<DhtRpcRequest>(json, _jsonOpts);
            if (req == null) return;

            // 更新路由表
            if (!string.IsNullOrEmpty(req.SenderId))
            {
                _routing.AddOrUpdate(new DhtNode
                {
                    Id = NodeId.FromHex(req.SenderId),
                    Endpoint = sender
                });
            }

            var response = await HandleRpcAsync(req, sender, ct);
            if (response != null)
            {
                response.RpcId = req.Id;
                await SendUdpAsync(response, sender, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DHT 消息处理失败");
        }
    }

    private Task<DhtRpcResponse?> HandleRpcAsync(DhtRpcRequest req, IPEndPoint sender, CancellationToken ct)
    {
        var resp = new DhtRpcResponse { SenderId = _routing.LocalId.ToString() };

        switch (req.Type)
        {
            case DhtRpcType.Ping:
                resp.Status = "ok";
                break;

            case DhtRpcType.FindNode:
                if (!string.IsNullOrEmpty(req.Target))
                {
                    var target = NodeId.FromHex(req.Target);
                    var closest = _routing.FindClosest(target);
                    resp.Nodes = closest.Select(n => new DhtNodeInfo
                    {
                        Id = n.Id.ToString(),
                        Address = n.Endpoint.Address.ToString(),
                        Port = n.Endpoint.Port
                    }).ToList();
                }
                break;

            case DhtRpcType.FindValue:
                if (!string.IsNullOrEmpty(req.Key))
                {
                    var value = _store.Get(req.Key);
                    if (value != null)
                    {
                        resp.Value = value;
                    }
                    else
                    {
                        var keyId = NodeId.FromString(req.Key);
                        var closest = _routing.FindClosest(keyId);
                        resp.Nodes = closest.Select(n => new DhtNodeInfo
                        {
                            Id = n.Id.ToString(),
                            Address = n.Endpoint.Address.ToString(),
                            Port = n.Endpoint.Port
                        }).ToList();
                    }
                }
                break;

            case DhtRpcType.Store:
                if (!string.IsNullOrEmpty(req.Key) && req.Value != null)
                {
                    _store.Put(req.Key, req.Value);
                    resp.Status = "ok";
                }
                break;
        }

        return Task.FromResult<DhtRpcResponse?>(resp);
    }

    private async Task<DhtRpcResponse?> SendRpcAsync(IPEndPoint target, DhtRpcRequest req)
    {
        var tcs = new TaskCompletionSource<DhtRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRpcs[req.Id] = tcs;

        try
        {
            await SendUdpAsync(req, target, CancellationToken.None);
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            cts.Token.Register(() => tcs.TrySetCanceled());
            return await tcs.Task;
        }
        catch
        {
            return null;
        }
        finally
        {
            _pendingRpcs.TryRemove(req.Id, out _);
        }
    }

    private async Task SendUdpAsync(object msg, IPEndPoint target, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(msg, _jsonOpts);
        await _udp!.SendAsync(bytes, bytes.Length, target);
    }

    /// <summary>后台维护：刷新路由表、重新发布存储</summary>
    private async Task MaintenanceLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), ct);

            // 刷新路由表 — 随机查找自己附近的节点
            var randomId = NodeId.Random();
            await IterativeFindNodeAsync(randomId);

            _logger.LogDebug("DHT 维护: {Summary}", _routing.Summary());
        }
    }

    private string NewRpcId() => Guid.NewGuid().ToString("N")[..16];

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}

// ── 配置 ──

public class DhtOptions
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 37825;
    public string NodeIdSeed { get; set; } = "catclaw-default-node";
    public List<string> BootstrapNodes { get; set; } = new() { "nas.08102516.xyz:37825" };
}

// ── RPC 消息 ──

public enum DhtRpcType
{
    Ping,
    FindNode,
    FindValue,
    Store
}

public class DhtRpcRequest
{
    public string Id { get; set; } = "";
    public DhtRpcType Type { get; set; }
    public string SenderId { get; set; } = "";
    public string? Target { get; set; }
    public string? Key { get; set; }
    public string? Value { get; set; }
}

public class DhtRpcResponse
{
    public string RpcId { get; set; } = "";
    public string SenderId { get; set; } = "";
    public string? Status { get; set; }
    public string? Value { get; set; }
    public List<DhtNodeInfo>? Nodes { get; set; }
}

public class DhtNodeInfo
{
    public string Id { get; set; } = "";
    public string Address { get; set; } = "";
    public int Port { get; set; }
}

// ── 并发键值存储 ──

public class ConcurrentKeyValueStore
{
    private readonly ConcurrentDictionary<string, (string Value, DateTime ExpiresAt)> _store = new();

    public int Count => _store.Count;

    public void Put(string key, string value, TimeSpan? ttl = null)
    {
        var expires = DateTime.UtcNow.Add(ttl ?? TimeSpan.FromHours(1));
        _store[key] = (value, expires);
    }

    public string? Get(string key)
    {
        if (_store.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > DateTime.UtcNow)
                return entry.Value;
            _store.TryRemove(key, out _);
        }
        return null;
    }

    public void Cleanup()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _store)
        {
            if (kv.Value.ExpiresAt <= now)
                _store.TryRemove(kv.Key, out _);
        }
    }
}
