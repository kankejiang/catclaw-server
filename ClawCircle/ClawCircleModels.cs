using System.Text.Json;
using System.Text.Json.Serialization;
using CatClawMusicServer.ClawCircle.Ledger;
using CatClawMusicServer.ClawCircle.Transfer;

namespace CatClawMusicServer.ClawCircle;

/// <summary>
/// 猫爪驿站跨网 P2P tracker / 信令协议（WebSocket JSON）。
/// 路径：ws(s)://host:port/ws/clawcircle?token=&deviceId=&name=
/// 每个文本帧是一个 JSON 对象，必须含 "type" 字段用于分发。
/// </summary>
public static class ClawCircleProtocol
{
    public const string Path = "/ws/clawcircle";

    // ── 客户端 → 服务端 ──
    public const string Register = "register";        // 宣告上线 + 曲库摘要
    public const string LibraryUpdate = "library_update"; // 更新曲库摘要
    public const string QueryPeer = "query_peer";     // 查询某个好友节点
    public const string FindSong = "find_song";       // 查询哪些在线节点拥有某首歌
    public const string Signal = "signal";             // 转发信令（SDP/ICE）给目标节点
    public const string Bye = "bye";                      // 主动下线

    // ── 服务端 → 客户端 ──
    public const string Welcome = "welcome";          // 握手成功，返回当前在线列表
    public const string PeerOnline = "peer_online";   // 有好友上线
    public const string PeerOffline = "peer_offline"; // 有好友下线
    public const string PeerUpdate = "peer_update";   // 某好友曲库摘要更新
    public const string PeerInfo = "peer_info";        // 对 query_peer 的答复
    public const string SongHolders = "song_holders"; // 对 find_song 的答复
    public const string Relay = "relay";                // 转发来的信令/中继数据
    public const string Error = "error";                  // 错误（如目标不在线）

    // ── 服务端 → 客户端（区块链账本）──
    public const string NewBlock = "new_block";        // 新区块产生（积分账本同步）
}

/// <summary>曲库摘要：节点上报给 tracker，用于 find_song 匹配。</summary>
public class LibrarySummary
{
    public int SongCount { get; set; }
    public int AlbumCount { get; set; }
    public int ArtistCount { get; set; }
    /// <summary>歌曲键列表（artist\u0001title 小写），供 find_song 命中。上限 5000，超出截断。</summary>
    public List<string> SongKeys { get; set; } = new();
}

/// <summary>在线节点信息（对外广播的快照）。</summary>
public class PeerInfo
{
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>公网可达地址（穿透成功时由客户端上报）；为空表示仅中继可用。</summary>
    public string? Wan { get; set; }
    public int? Port { get; set; }
    /// <summary>true=只能走服务端中继（NAT 穿透失败）。</summary>
    public bool RelayOnly { get; set; }
    public LibrarySummary? Library { get; set; }
    public DateTime ConnectedAt { get; set; }

    /// <summary>WebSocket 连接的源 IP（服务端内部使用，用于 STUN 源 IP 绑定校验，不序列化）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Net.IPAddress? WsIpAddress { get; set; }
}

// ── 客户端 → 服务端 消息 ──

public class RegisterMsg
{
    public string Type { get; set; } = ClawCircleProtocol.Register;
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Wan { get; set; }
    public int? Port { get; set; }
    public bool RelayOnly { get; set; }
    public LibrarySummary? Library { get; set; }
}

public class LibraryUpdateMsg
{
    public string Type { get; set; } = ClawCircleProtocol.LibraryUpdate;
    public LibrarySummary Library { get; set; } = new();
}

public class QueryPeerMsg
{
    public string Type { get; set; } = ClawCircleProtocol.QueryPeer;
    public string DeviceId { get; set; } = "";
}

public class FindSongMsg
{
    public string Type { get; set; } = ClawCircleProtocol.FindSong;
    public string SongKey { get; set; } = "";
}

public class SignalMsg
{
    public string Type { get; set; } = ClawCircleProtocol.Signal;
    public string To { get; set; } = "";
    public JsonElement Data { get; set; }
}

public class ByeMsg
{
    public string Type { get; set; } = ClawCircleProtocol.Bye;
}

// ── 服务端 → 客户端 消息 ──

public class WelcomeMsg
{
    public string Type { get; set; } = ClawCircleProtocol.Welcome;
    public string You { get; set; } = "";
    public List<PeerInfo> Peers { get; set; } = new();
    public long ServerTime { get; set; }
}

public class PeerOnlineMsg
{
    public string Type { get; set; } = ClawCircleProtocol.PeerOnline;
    public PeerInfo Peer { get; set; } = new();
}

public class PeerOfflineMsg
{
    public string Type { get; set; } = ClawCircleProtocol.PeerOffline;
    public string DeviceId { get; set; } = "";
}

public class PeerUpdateMsg
{
    public string Type { get; set; } = ClawCircleProtocol.PeerUpdate;
    public string DeviceId { get; set; } = "";
    public LibrarySummary? Library { get; set; }
}

public class PeerInfoMsg
{
    public string Type { get; set; } = ClawCircleProtocol.PeerInfo;
    public PeerInfo Peer { get; set; } = new();
}

public class SongHoldersMsg
{
    public string Type { get; set; } = ClawCircleProtocol.SongHolders;
    public string SongKey { get; set; } = "";
    /// <summary>持有该歌曲的在线节点（含 wan/port 供 NAT 打洞直连）。</summary>
    public List<PeerInfo> Holders { get; set; } = new();
}

public class RelayMsg
{
    public string Type { get; set; } = ClawCircleProtocol.Relay;
    public string From { get; set; } = "";
    public JsonElement Data { get; set; }
}

public class ErrorMsg
{
    public string Type { get; set; } = ClawCircleProtocol.Error;
    public string ErrorText { get; set; } = "";
}

/// <summary>新区块广播（积分账本同步）。</summary>
public class NewBlockMsg
{
    public string Type { get; set; } = ClawCircleProtocol.NewBlock;
    public Block Block { get; set; } = new();
}

// ── P2P 传输信令（通过 signal 消息透传，Data 字段放以下 JSON 结构）──
// 客户端将下列对象序列化后作为 SignalMsg.Data 发送；服务端不解析内容，仅转发。
// 接收方根据 kind 字段分发处理。

/// <summary>传输信令种类。</summary>
public static class TransferSignalKind
{
    public const string Request = "transfer_request";   // 接收方→发送方：请求下载文件
    public const string Accept = "transfer_accept";      // 发送方→接收方：同意，含 manifest + taskId
    public const string Reject = "transfer_reject";      // 发送方→接收方：拒绝（忙/无此文件）
    public const string Complete = "transfer_complete";  // 接收方→发送方：全部 chunk 接收完毕
    public const string Cancel = "transfer_cancel";       // 双向：取消传输
    public const string HolePunchReady = "holepunch_ready"; // 双向：反射端点已就绪，开始打洞
}

/// <summary>传输信令基类（嵌入 signal.data）。</summary>
public class TransferSignal
{
    public string Kind { get; set; } = "";
}

/// <summary>请求下载文件（接收方发起）。songKey 用于发送方定位文件。</summary>
public class TransferRequestSignal : TransferSignal
{
    public string TaskId { get; set; } = "";        // 接收方生成的全局唯一任务 ID
    public string SongKey { get; set; } = "";       // artist\u0001title 小写
    public string FileName { get; set; } = "";      // 期望的文件名
    public string? MyWan { get; set; }              // 接收方反射端点 IP（STUN 探测得到）
    public int? MyPort { get; set; }                // 接收方反射端点端口
}

/// <summary>发送方同意传输，返回文件清单。</summary>
public class TransferAcceptSignal : TransferSignal
{
    public string TaskId { get; set; } = "";
    public PieceManifest? Manifest { get; set; }    // 文件分块清单
    public string? MyWan { get; set; }              // 发送方反射端点 IP
    public int? MyPort { get; set; }                // 发送方反射端点端口
    public bool RelayFallback { get; set; }         // true=打洞失败需走 WebSocket 中继
}

/// <summary>发送方拒绝传输。</summary>
public class TransferRejectSignal : TransferSignal
{
    public string TaskId { get; set; } = "";
    public string Reason { get; set; } = "";
}

/// <summary>传输完成通知。</summary>
public class TransferCompleteSignal : TransferSignal
{
    public string TaskId { get; set; } = "";
}

/// <summary>取消传输。</summary>
public class TransferCancelSignal : TransferSignal
{
    public string TaskId { get; set; } = "";
    public string Reason { get; set; } = "";
}

/// <summary>反射端点就绪，通知对方开始打洞。</summary>
public class HolePunchReadySignal : TransferSignal
{
    public string TaskId { get; set; } = "";
    public string? Wan { get; set; }
    public int? Port { get; set; }
}
