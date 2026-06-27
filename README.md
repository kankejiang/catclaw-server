# CatClaw Music Server (C# / ASP.NET Core)

P2P 音乐流媒体服务器，使用 C# + ASP.NET Core + SQLite 重写版。

## 功能特性
- 🎵 音乐扫描：支持 MP3/FLAC/WAV/WMA/OGG/AIFF/M4A/APE/WV/MP4/MP2/MPC/TTA/OPUS
- 🔍 全文搜索：按歌曲名、艺术家、专辑搜索
- 📀 分页 API：所有列表支持分页
- 🎧 流媒体播放：支持 HTTP Range（断点续传）
- 📋 播放列表管理
- ⭐ 收藏 & 播放历史
- 🌐 Web UI：内置管理面板 + 播放器
- 🚀 Swagger API 文档

## 快速开始

### 1. 用 Visual Studio 打开
双击 `CatClawMusicServer.csproj` 或用 VS → "打开项目"

### 2. 还原 NuGet 包
VS 会自动还原，或手动：
```bash
dotnet restore
```

### 3. 配置音乐目录
编辑 `appsettings.json`：
```json
{
  "MusicServer": {
    "MusicDirectory": "D:\\YourMusicFolder",
    "CoverOutputDir": "Data\\covers"
  }
}
```

### 4. 运行
按 F5 或 `dotnet run`

访问 `http://localhost:37823` 查看 Web UI
访问 `http://localhost:37823/swagger` 查看 API 文档

## API 端点

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | /api/songs | 歌曲列表（分页） |
| GET | /api/songs/{id} | 歌曲详情 |
| GET | /api/songs/{id}/stream | 流媒体播放 |
| GET | /api/songs/{id}/cover | 封面图 |
| GET | /api/songs/{id}/lyrics | 歌词文本 |
| GET | /api/artists | 艺术家列表 |
| GET | /api/albums | 专辑列表 |
| GET | /api/search?q=xxx | 搜索 |
| GET/POST/DELETE | /api/playlists | 播放列表管理 |
| GET/POST/DELETE | /api/favorites | 收藏管理 |
| GET/POST/DELETE | /api/history | 播放历史 |

## 项目结构
```
Controllers/   → API 控制器
Models/        → 数据模型
Data/          → EF Core DbContext
Services/      → 业务逻辑（扫描、标签读取）
wwwroot/       → 静态文件（Web UI）
```

## 依赖
- .NET 8.0
- Entity Framework Core (SQLite)
- TagLibSharp (音频标签)
- Swashbuckle (Swagger)
- AspNetCoreRateLimit (限速)

## Windows 命令行程序（自包含 EXE）
本服务同时是一个命令行程序，可直接在 Windows（含 NAS/未装 .NET 运行时的机器）上运行。

### 发布自包含 EXE
```bash
dotnet publish CatClawMusicServer.csproj -c Release -r win-x64 --self-contained -o publish
# 产物：publish/CatClawMusicServer.exe（约 110MB，已捆绑 .NET 8 运行时）
```

### 命令行参数
| 参数 | 说明 | 默认值 |
|------|------|--------|
| `--music-dir <路径>` | 音乐库目录（递归扫描） | `MusicServer:MusicDirectory` 配置 |
| `--cover-dir <路径>` | 封面输出目录 | `MusicServer:CoverOutputDir` |
| `--db-path <路径>` | SQLite 数据库文件 | `MusicServer:DbPath` |
| `--token <字符串>` | 内网访问令牌（Subsonic 密码 / Bearer） | `MusicServer:AccessToken`（空=关闭鉴权） |
| `--port <端口>` | Kestrel 监听端口 | `37823` |
| `--scan-and-exit` | 仅扫描音乐库后退出（不启动 Web 服务） | 否 |

### 运行示例
```bash
# Web 服务模式（媒体中心 / Subsonic 兼容服务）
CatClawMusicServer.exe --music-dir "D:\Music" --token "强随机令牌" --port 37823

# 仅建库模式（一次性扫描，可用于定时任务 / 无界面索引）
CatClawMusicServer.exe --music-dir "D:\Music" --scan-and-exit
```

### 客户端连接（手机端 CatClawMusic）
「远程音乐」→ 协议选 **Navidrome**，地址填 `http://<NAS内网IP>:37823`，
用户名任意，密码填上面 `--token` 的值即可直连听歌 / 同步。

## NAS / Docker 部署
仓库已附带 `Dockerfile` 与 `docker-compose.yml`（端口 `37823`）。在 NAS 上：
```bash
docker compose up -d
# 然后改 docker-compose.yml 里的 MusicServer__AccessToken 为强令牌
```
未安装 Docker 的 Windows 机器可直接使用上面的自包含 EXE。

## 猫爪圈（ClawCircle）P2P 信令（Stage 2）

猫爪圈是跨网 P2P 音乐共享功能。服务端在这里扮演 **tracker / 信令中转（WebSocket hub）** 角色：
维护在线节点注册表、转发 WebRTC 信令（SDP/ICE）、按曲库摘要匹配某首歌的持有者。

### 连接与鉴权
- 路径：`ws://<host>:37823/ws/clawcircle`
- 鉴权：与 Web API 共用同一个 `AccessToken`，支持两种携带方式：
  - 查询参数：`?token=<AccessToken>`
  - 请求头：`Authorization: Bearer <AccessToken>`
  - 未配置 `AccessToken` 时免鉴权（本地开发）。
- 错误 token → HTTP `401`；非 WebSocket 请求 → HTTP `426`。

### 消息协议
每个 WebSocket 文本帧是一个 JSON 对象，必须含 `type` 字段用于分发（camelCase）。

**客户端 → 服务端**
| type | 字段 | 说明 |
|------|------|------|
| `register` | `deviceId, name, wan?, port?, relayOnly?, library?` | 宣告上线 + 曲库摘要（曲库摘要用于 `find_song`） |
| `library_update` | `library` | 更新曲库摘要（变更后广播给好友） |
| `query_peer` | `deviceId` | 查询某个好友节点信息 |
| `find_song` | `songKey` | 查询哪些在线节点拥有某首歌（键格式 `artist\u0001title` 小写） |
| `signal` | `to, data` | 转发 WebRTC 信令（SDP/ICE）给目标节点 |
| `bye` | — | 主动下线 |

**服务端 → 客户端**
| type | 字段 | 说明 |
|------|------|------|
| `welcome` | `you, peers, serverTime` | 握手成功，返回当前其他在线节点 |
| `peer_online` | `peer` | 有好友上线 |
| `peer_offline` | `deviceId` | 有好友下线 |
| `peer_update` | `deviceId, library` | 某好友曲库摘要更新 |
| `peer_info` | `peer` | 对 `query_peer` 的答复 |
| `song_holders` | `songKey, holders` | 对 `find_song` 的答复（持有者 deviceId 列表） |
| `relay` | `from, data` | 转发来的信令 / 中继数据 |
| `error` | `errorText` | 错误（如目标不在线、未知类型） |

### 调试端点
- `GET /api/clawcircle/peers`（Bearer 鉴权）：返回当前在线节点数 `onlineCount` 与节点快照 `peers`，便于运维查看 tracker 状态。

> 说明：Stage 2 仅实现服务端 tracker / 信令转发。真正的客户端分片传输、做种与 NAT 穿透（Stage 3）在 CatClawMusic 客户端内实现，不在本服务端仓库。

