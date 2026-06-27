using System.Text.Json;
using System.Text.Json.Serialization;

namespace CatClawMusicServer.ClawCircle;

/// <summary>
/// 猫爪圈跨网 P2P tracker / 信令协议（WebSocket JSON）。
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
