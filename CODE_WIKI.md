# CatClawMusicServer Code Wiki

> P2P 音乐流媒体服务器 · C# / ASP.NET Core 8 / SQLite

---

## 目录

- [1. 项目概述](#1-项目概述)
- [2. 技术栈与依赖](#2-技术栈与依赖)
- [3. 项目目录结构](#3-项目目录结构)
- [4. 整体架构](#4-整体架构)
- [5. 启动与配置流程](#5-启动与配置流程)
- [6. 数据层（Models + EF Core）](#6-数据层models--ef-core)
- [7. 业务服务层（Services）](#7-业务服务层services)
- [8. API 控制器层](#8-api-控制器层)
- [9. ClawCircle P2P 模块](#9-clawcircle-p2p-模块)
- [10. 中间件与鉴权体系](#10-中间件与鉴权体系)
- [11. Web UI 前端](#11-web-ui-前端)
- [12. 部署与运行方式](#12-部署与运行方式)
- [13. 关键业务流程](#13-关键业务流程)

---

## 1. 项目概述

**CatClawMusicServer** 是一个自托管的音乐流媒体服务器，定位类似 Navidrome / Jellyfin 的音频子集，并额外内置了「猫爪驿站（ClawCircle）」跨网 P2P 音乐共享能力。

核心能力：

- 🎵 音乐库扫描：支持 14 种音频格式（MP3/FLAC/WAV/WMA/OGG/AIFF/M4A/APE/WV/MP4/MP2/MPC/TTA/OPUS），读取 TagLibSharp 标签元数据并入库
- 🔍 三维度搜索：按歌曲名 / 艺术家 / 专辑
- 🎧 流媒体播放：HTTP Range 断点续传 + FFmpeg 实时转码（HLS / 直出 opus/mp3/aac）
- 📋 播放列表 / 收藏 / 评分 / 播放队列 / Scrobble 听歌记录
- 🎯 智能推荐：基于播放历史的混合推荐引擎（无需外部 ML 模型）
- 📊 统计：个人统计 + 全服统计 + 每日预聚合
- 🌐 双协议 API：自有 V1 REST API（JWT）+ Subsonic 兼容 API（`/rest/*`）
- 🔗 猫爪驿站 P2P：WebSocket 信令中转 + STUN NAT 反射 + Kademlia DHT 节点发现 + 分块传输引擎
- 🖥️ 内置 Web UI（Vue 3 SPA）+ Swagger 文档
- 🐳 Docker 一键部署 / Windows 自包含 EXE

---

## 2. 技术栈与依赖

| 层 | 技术 |
|----|------|
| 运行时 | .NET 8.0 (ASP.NET Core) |
| 数据库 | SQLite via Entity Framework Core 8.0.11 |
| 认证 | JWT Bearer (`Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.11) + BCrypt 密码哈希 (`BCrypt.Net-Next` 4.0.3) |
| 音频标签 | TagLibSharp 2.3.0（ID3/FLAC/APE 等元数据读取） |
| 转码 | FFmpeg（外部进程，HLS 分片 / 直出） |
| API 文档 | Swashbuckle.AspNetCore 6.6.2 |
| 限速 | .NET 8 内置 `System.Threading.RateLimiting`（每 IP 200 req/min） |
| 前端 | Vue 3 + Vue Router 4（ESM import map）+ HLS.js |
| 部署 | Docker（多阶段构建）+ GitHub Actions（GHCR 自动推送） |

项目文件：[CatClawMusicServer.csproj](file:///d:/Code/CatClawMusicServer/CatClawMusicServer.csproj)

---

## 3. 项目目录结构

```
CatClawMusicServer/
├── Program.cs                      # 入口：配置加载、DI 注册、中间件管道、CLI 模式
├── CatClawMusicServer.csproj       # 项目定义与依赖
├── appsettings.json                # 主配置文件
├── Dockerfile / docker-compose.yml # 容器化部署
├── install*.sh / update.sh         # 一键部署脚本
│
├── *.cs (根目录)                   # 选项类 + 中间件 + 凭据存储
│   ├── ScannerOptions.cs           # 扫描配置 record
│   ├── StreamingOptions.cs         # 流媒体/转码配置
│   ├── JwtOptions.cs               # JWT 配置
│   ├── ServerAuthOptions.cs        # 内网鉴权配置
│   ├── AdminCredentialStore.cs     # 管理员凭据持久化（Data/admin.json）
│   ├── ApiAuthMiddleware.cs        # /api 与 /rest 鉴权中间件
│   ├── WebUiAuthMiddleware.cs      # Web UI 静态页鉴权中间件
│   └── EventWebSocketMiddleware.cs # /ws/events 事件推送中间件
│
├── Data/
│   └── ApplicationDbContext.cs     # EF Core DbContext（16 张表 + 关系 + 索引）
│
├── Models/                         # 数据模型（16 个实体）
│   ├── Song.cs / Artist.cs / Album.cs
│   ├── Playlist.cs / PlaylistSong.cs / Favorite.cs / PlayHistory.cs
│   ├── User.cs / RefreshToken.cs / Device.cs
│   ├── Genre.cs / SongGenre.cs / Rating.cs / Scrobble.cs
│   ├── PlayQueue.cs / StatsDaily.cs
│   └── ApiResponse.cs              # 统一响应信封 + 错误码常量
│
├── Services/                       # 业务逻辑服务
│   ├── MusicScanner.cs             # 音乐库全量/增量扫描
│   ├── FileTagService.cs           # TagLibSharp 标签读取 + 歌词/封面查找
│   ├── JwtService.cs               # JWT 生成 / 验证 / Claim 提取
│   ├── TranscodingService.cs       # FFmpeg 转码任务管理（HLS + 直出）
│   ├── CoverService.cs             # 封面缩略图生成（FFmpeg scale）
│   ├── LyricsParser.cs             # LRC / TTML 歌词解析
│   ├── RecommendService.cs         # 智能推荐引擎
│   ├── StatsService.cs             # 播放统计聚合
│   └── EventBus.cs                 # WebSocket 事件总线
│
├── Controllers/                    # 三层 API 控制器
│   ├── (根目录)                    # V0 旧版 API（/api/*，AccessToken 鉴权）
│   │   ├── SongsController.cs / ArtistsController.cs / AlbumsController.cs
│   │   ├── PlaylistsController.cs / FavoritesController.cs / HistoryController.cs
│   │   ├── SearchController.cs / ScanController.cs / AuthController.cs
│   │   ├── ConfigController.cs / ClawCircleController.cs
│   ├── V1/                         # V1 新版 API（/api/v1/*，JWT 鉴权，16 个控制器）
│   │   ├── AuthController.cs / AdminController.cs / LibraryController.cs
│   │   ├── SongsController.cs / AlbumsController.cs / ArtistsController.cs
│   │   ├── PlaylistController.cs / PlayQueueController.cs
│   │   ├── FavoritesController.cs / RatingsController.cs / ScrobbleController.cs
│   │   ├── SearchController.cs / GenreController.cs / StatsController.cs
│   │   ├── RecommendController.cs / HlsController.cs
│   └── Subsonic/                   # Subsonic 兼容层（/rest/*，partial class 拆 9 文件）
│       ├── SubsonicBase.cs         # 基类：响应构建器 + DTO 转换
│       ├── SubsonicAlbums.cs / SubsonicAnnotations.cs / SubsonicBookmarks.cs
│       ├── SubsonicBrowsing.cs / SubsonicMedia.cs / SubsonicPlaylists.cs
│       └── SubsonicSearch.cs / SubsonicSystem.cs
│
├── ClawCircle/                     # P2P 子系统
│   ├── ClawCircleTracker.cs        # 在线节点注册表（单例）
│   ├── ClawCircleWebSocketMiddleware.cs  # WebSocket 信令中间件
│   ├── ClawCircleStunService.cs    # STUN UDP 反射服务
│   ├── ClawCircleModels.cs         # 信令协议常量 + 消息 DTO
│   ├── NodeReputation.cs           # 节点信誉系统
│   ├── BloomFilter.cs              # 布隆过滤器（曲库摘要）
│   ├── Dht/
│   │   ├── DhtService.cs           # Kademlia DHT 服务
│   │   ├── KBucket.cs              # K-Bucket 路由表
│   │   └── NodeId.cs               # 160 位节点 ID（SHA-1）
│   └── Transfer/
│       └── TransferEngine.cs       # P2P 分块传输引擎
│
└── wwwroot/                        # 静态 Web UI
    ├── index.html                  # Vue SPA 入口
    ├── login.html / register.html
    ├── css/style.css
    └── js/ (api.js / app.js / player.js / store.js)
```

---

## 4. 整体架构

CatClawMusicServer 是一个单体分层架构的应用，但内部分成三个相对独立的 API 协议层 + 一个 P2P 子系统。

### 4.1 分层架构图

```
┌─────────────────────────────────────────────────────────────────┐
│                       客户端                                    │
│  CatClawMusic（MAUI 手机端） │ Web UI（Vue 3）│ Subsonic 客户端 │
└──────────┬─────────────────────┬──────────────────┬────────────┘
           │                     │                  │
   ┌───────▼────────┐    ┌──────▼───────┐   ┌──────▼────────┐
   │  V1 REST API   │    │  V0 REST API │   │ Subsonic API  │
   │ /api/v1/* (JWT)│    │ /api/* (Token)│   │ /rest/* (Token)│
   └───────┬────────┘    └──────┬───────┘   └──────┬────────┘
           │                    │                  │
   ┌───────▼────────────────────▼──────────────────▼────────┐
   │              ASP.NET Core 中间件管道                    │
   │  CORS → WebSockets → ClawCircleWS → ApiAuth → WebUiAuth │
   │  → StaticFiles → RateLimiter → JWT Auth → Controllers   │
   └───────┬────────────────────┬──────────────────┬────────┘
           │                    │                  │
   ┌───────▼───────┐   ┌────────▼────────┐  ┌──────▼─────────┐
   │  Services 层  │   │  EventBus / WS  │  │ ClawCircle P2P │
   │ Scanner/Jwt/  │   │  事件推送        │  │ Tracker/STUN/  │
   │ Transcode/    │   │                 │  │ DHT/Transfer   │
   │ Recommend...  │   │                 │  │                │
   └───────┬───────┘   └─────────────────┘  └────────────────┘
           │
   ┌───────▼───────────────────────┐
   │  EF Core (SQLite)             │
   │  ApplicationDbContext         │
   │  16 张表 + 关系 + 索引         │
   └───────────────────────────────┘
```

### 4.2 三层 API 设计

项目同时维护三套 API 端点，对应不同协议与鉴权方式：

| API 层 | 路由前缀 | 鉴权方式 | 响应格式 | 用途 |
|--------|----------|----------|----------|------|
| **V0 旧版** | `/api/*` | AccessToken (Bearer) 或 cookie | 裸 JSON | 早期简化 API，向后兼容 |
| **V1 新版** | `/api/v1/*` | JWT Bearer | `ApiResponse<T>` 信封 | 主力 API，含完整用户体系 |
| **Subsonic** | `/rest/*` | Subsonic token (t=md5(pwd+salt)) | `subsonic-response` | 兼容 Navidrome/Subsonic 客户端 |

> 三层共用同一个 `ApplicationDbContext` 数据层，写操作最终落到同一套 SQLite 表。

### 4.3 P2P 子系统（ClawCircle）

ClawCircle 是独立的横向能力，服务端在这里只扮演 **tracker / 信令中转 + DHT 节点 + STUN 反射** 角色。真正的 P2P 分片传输在客户端实现，但本仓库提供了 `TransferEngine` 作为协议参考实现。

---

## 5. 启动与配置流程

入口文件：[Program.cs](file:///d:/Code/CatClawMusicServer/Program.cs)

### 5.1 启动流程

```
ParseCliArgs(args)                 # 解析 --music-dir / --token / --port / --scan-and-exit / claw reset
  ↓
读取配置 (appsettings.json + CLI 覆盖)
  ↓
注册 DI 服务：
  ├─ DbContextFactory (Singleton) + DbContext (Scoped)
  ├─ MusicScanner (Scoped) + ScannerOptions (Singleton)
  ├─ ServerAuthOptions / AdminCredentialStore / JwtOptions / JwtService
  ├─ ClawCircleTracker / NodeReputation / TransferEngine (Singleton)
  ├─ DhtService (Singleton, 可选)
  ├─ StreamingOptions / TranscodingService / CoverService / EventBus
  ├─ StatsService / RecommendService (Scoped)
  ├─ JWT Bearer 认证 + Authorization
  ├─ MVC Controllers + Swagger + CORS + RateLimiter
  ↓
builder.Build()
  ↓
CLI 模式分支：
  ├─ claw reset        → 删除 Data/admin.json 后退出
  └─ --scan-and-exit   → 扫描音乐库后退出（不启动 Web 服务）
  ↓
配置中间件管道（顺序敏感）：
  Swagger(Dev) → CORS → WebSockets → ClawCircleWebSocketMiddleware
  → ApiAuthMiddleware → WebUiAuthMiddleware → DefaultFiles → StaticFiles
  → RateLimiter → Authentication → Authorization → MapControllers
  ↓
EnsureCreated() 数据库
  ↓
启动 STUN 服务（UDP 端口 = HTTP 端口 + 1）
  ↓
启动 DHT 服务（可选，UDP 37825）+ Bootstrap
  ↓
app.Run()
```

### 5.2 配置文件

主配置：[appsettings.json](file:///d:/Code/CatClawMusicServer/appsettings.json)

| 配置节 | 关键字段 | 说明 |
|--------|----------|------|
| `MusicServer` | `DbPath` / `MusicDirectory` / `CoverOutputDir` / `AccessToken` / `AdminUser` / `AdminPassword` | 数据库路径、音乐库目录、内网访问令牌、管理员账号 |
| `Jwt` | `SecretKey` / `Issuer` / `Audience` / `AccessTokenExpireMinutes`(15) / `RefreshTokenExpireDays`(30) | JWT 签发参数 |
| `Streaming` | `HlsEnabled` / `FFmpegPath` / `TranscodeCacheSizeGB`(2) / `SegmentDurationSeconds`(6) / `MaxConcurrentTranscodes`(4) / `DefaultBitrates`(96,160,256) / `TranscodeDir` | 转码与 HLS 配置 |
| `ClawCircle` | `DhtEnabled` / `DhtPort`(37825) / `NodeIdSeed` / `BootstrapNodes` | P2P DHT 配置 |
| `IpRateLimiting` | `GeneralRules` | 限速规则（200/min 全局，stream 60/min） |

所有配置均可通过环境变量覆盖（`__` 分隔，如 `MusicServer__AccessToken`），Docker 部署主要靠环境变量。

### 5.3 CLI 参数

| 参数 | 说明 |
|------|------|
| `--music-dir <path>` | 音乐库目录 |
| `--cover-dir <path>` | 封面输出目录 |
| `--db-path <path>` | SQLite 数据库文件 |
| `--token <string>` | 内网访问令牌 |
| `--port <int>` | Kestrel 监听端口（默认 37823） |
| `--scan-and-exit` | 仅扫描后退出 |
| `claw reset` | 重置管理员凭据 |

---

## 6. 数据层（Models + EF Core）

### 6.1 DbContext

文件：[Data/ApplicationDbContext.cs](file:///d:/Code/CatClawMusicServer/Data/ApplicationDbContext.cs)

- 通过 `IDbContextFactory<ApplicationDbContext>` 注册为 Singleton（线程安全，支持后台扫描任务并发使用）
- Scoped `ApplicationDbContext` 从工厂创建
- `OnModelCreating` 中显式禁用表名复数化、定义全部外键关系（Cascade 删除）、唯一索引、复合主键
- 启动时调用 `db.Database.EnsureCreated()` 建库（无 Migrations）

### 6.2 实体关系图

```
User ──┬── RefreshToken (1:N)
       ├── Device (1:N, 唯一约束 UserId+DeviceId)
       ├── Playlist (1:N) ──┬── PlaylistSong (N:1) ── Song
       │                    └── (复合唯一 PlaylistId+SongId)
       ├── Favorite (1:N) ──── Song (唯一约束 UserId+SongId)
       ├── Rating (1:N) ────── Song (唯一约束 UserId+SongId, 1-5 分)
       ├── Scrobble (1:N) ──── Song (索引 UserId+Timestamp)
       ├── PlayQueue (1:1, 唯一约束 UserId)
       └── StatsDaily (1:N, 唯一约束 UserId+Date)

Artist ──┬── Album (1:N) ──── Song
         └── Song (1:N)

Song ──┬── SongGenre (N:N) ── Genre (Genre.Name 唯一)
       ├── PlaylistSong / Favorite / PlayHistory / Scrobble / Rating
       └── 属性: Title/Duration/FilePath/Bitrate/Year/Genre
                 CoverArtPath/LyricsPath/FileHash
                 SampleRate/BitDepth/Channels/Codec/IsLossless/DiscNumber
```

### 6.3 核心模型

| 模型 | 文件 | 关键字段 |
|------|------|----------|
| [Song](file:///d:/Code/CatClawMusicServer/Models/Song.cs) | Song.cs | Id, Title, ArtistId, AlbumId, Duration, FilePath, FileSize, Bitrate, TrackNumber, Year, Genre, CoverArtPath, LyricsPath, SampleRate, BitDepth, Channels, Codec, IsLossless, DiscNumber, FileHash |
| [User](file:///d:/Code/CatClawMusicServer/Models/User.cs) | User.cs | Id, Username(唯一), PasswordHash(BCrypt), DisplayName, Role("admin"\|"user"), AvatarPath, CreatedAt, LastLoginAt |
| [Playlist](file:///d:/Code/CatClawMusicServer/Models/Playlist.cs) | Playlist.cs | Id, UserId, Name, Description, IsPublic, CreatedAt, UpdatedAt |
| [Scrobble](file:///d:/Code/CatClawMusicServer/Models/Scrobble.cs) | Scrobble.cs | Id, UserId, SongId, Timestamp, DurationPlayedMs, Source("library"\|"playlist"\|"search"\|"recommend"\|"p2p") |
| [PlayQueue](file:///d:/Code/CatClawMusicServer/Models/PlayQueue.cs) | PlayQueue.cs | Id, UserId(唯一), SongIds(JSON 数组), CurrentIndex, PositionMs |
| [ApiResponse](file:///d:/Code/CatClawMusicServer/Models/ApiResponse.cs) | ApiResponse.cs | 统一响应信封 `{code, message, data}` + `PagedResult<T>` + `ErrorCodes` 常量（1xxx 认证 / 2xxx 参数 / 3xxx 资源 / 4xxx 服务端） |

---

## 7. 业务服务层（Services）

### 7.1 MusicScanner — 音乐库扫描

文件：[Services/MusicScanner.cs](file:///d:/Code/CatClawMusicServer/Services/MusicScanner.cs)

| 方法 | 职责 |
|------|------|
| `ScanDirectoryAsync(dir, coverDir, ct)` | 全量扫描：遍历 14 种扩展名文件，读取标签 → EnsureArtist → EnsureAlbum → 新增/更新 Song。每 100 个文件 SaveChanges 一次避免大事务 |
| `IncrementalScanAsync(...)` | 增量扫描：基于 `FileHash`（前 64KB + 文件大小 + 修改时间的 FNV-1a）跳过未变文件 |
| `ComputeFileHash(filePath)` | 私有：计算轻量哈希用于增量判断 |
| `EnsureArtistAsync` / `EnsureAlbumAsync` | 私有：确保艺术家/专辑存在，不存在则创建 |

返回 `ScanResult { ProcessedCount, AddedCount, UpdatedCount, SkippedCount, ErrorCount }`。

### 7.2 FileTagService — 标签读取

文件：[Services/FileTagService.cs](file:///d:/Code/CatClawMusicServer/Services/FileTagService.cs)

静态类，封装 TagLibSharp。

| 方法 | 职责 |
|------|------|
| `ReadTags(filePath, coverOutputDir)` | 读取 Title/Artist/Album/Duration/Bitrate/TrackNumber/Year/Genre；提取首张封面图保存为文件 |
| `FindLyricsFile(audioPath)` | 查找同目录 `.lrc` / `.ttml` 歌词文件 |
| `FindCoverFile(audioPath)` | 查找同目录 `{basename}.{jpg,png,webp}` 或 `cover.*` / `folder.*` |

返回 `AudioTags` record。

### 7.3 JwtService — JWT 认证

文件：[Services/JwtService.cs](file:///d:/Code/CatClawMusicServer/Services/JwtService.cs)

| 方法 | 职责 |
|------|------|
| `GenerateAccessToken(userId, username, role)` | 签发短期 Access Token（HmacSha256，默认 15 分钟），claims 含 sub/unique_name/role/jti/iat |
| `GenerateRefreshToken()` | 生成 64 字节随机 Refresh Token（Base64，默认 30 天） |
| `ValidateToken(token)` | 验证签名 + 签发方 + 受众 + 生命周期，返回 `ClaimsPrincipal?` |
| `GetUserId/GetUsername/GetRole(principal)` | 静态：从 Claims 提取用户信息 |

### 7.4 TranscodingService — FFmpeg 转码

文件：[Services/TranscodingService.cs](file:///d:/Code/CatClawMusicServer/Services/TranscodingService.cs)

单例，管理 FFmpeg 进程生命周期 + HLS 分片 + LRU 缓存淘汰。参考 Jellyfin 的 TranscodeManager 模式。

| 方法 | 职责 |
|------|------|
| `GetOrStartHlsJobAsync(songId, filePath, bitrate, ct)` | 获取或启动 HLS 转码任务（双重检查锁 + 全局并发信号量 `MaxConcurrentTranscodes`）。若 `index.m3u8` 含 `#EXT-X-ENDLIST` 则直接复用完整缓存 |
| `GetOrStartStreamJobAsync(songId, filePath, format, bitrate, ct)` | 启动直出转码（opus/mp3/aac 到 stdout），非 HLS |
| `WaitForSegmentsAsync(outputDir, minSegments, ct)` | 轮询 `index.m3u8` 直到至少 N 个分片就绪（默认超时 30s） |
| `BuildMasterPlaylist(songBitrate, isLossless, bitrates)` | 生成多码率 master m3u8（跳过高于源码率的档位，无损直出 "original"） |
| `BuildOriginalPlaylist(filePath, durationSec)` | 生成原始文件直通 playlist（单分片） |
| `RunCacheCleanup()` | 定时（10min）：缓存超 `TranscodeCacheSizeGB` 时按 LRU 删除最旧文件至 80% |
| `KillIdleJobs()` | 定时（30s）：超过 `IdleKillSeconds`(60) 未访问的进程发 'q' 优雅终止，3s 后强杀 |

数据结构：`TranscodingJob { Key, SongId, Bitrate, OutputDir, Process, Status, ExitCode, StartedAt, LastAccessedAt, IsRunning }`，`TranscodeStatus` 枚举（Pending/Running/Complete/Error）。

### 7.5 CoverService — 封面缩略图

文件：[Services/CoverService.cs](file:///d:/Code/CatClawMusicServer/Services/CoverService.cs)

| 方法 | 职责 |
|------|------|
| `GetCoverPath(originalPath, size)` | 返回指定尺寸封面路径。size: `small`(120) / `medium`(300) / `large`(600) / `original`。缓存键 = MD5(原路径)[..16] + 尺寸。用 FFmpeg `scale` 生成缩略图（避免 System.Drawing 跨平台依赖），失败回退原图 |

### 7.6 LyricsParser — 歌词解析

文件：[Services/LyricsParser.cs](file:///d:/Code/CatClawMusicServer/Services/LyricsParser.cs)

静态类（`partial` + `GeneratedRegex` 编译时正则），支持三种格式：

| 格式 | 解析逻辑 |
|------|----------|
| LRC | 匹配 `[mm:ss.xx]` 时间标签，按时间排序；非时间标签行作为翻译 |
| TTML/AMLL | 匹配 `<p begin="...">text</p>`，提取逐字 `<span begin="...">word</span>` 时间轴 |
| 纯文本 | 直接返回 |

返回 `LyricsResult { Type, Synced, Content, Translation, Lines[] }`，`LyricsLine { StartTimeMs, Text, Words[]? }`，`LyricsWord { StartTimeMs, Text }`。

### 7.7 RecommendService — 智能推荐

文件：[Services/RecommendService.cs](file:///d:/Code/CatClawMusicServer/Services/RecommendService.cs)

基于播放历史的混合推荐引擎，无需外部 ML 模型。

| 方法 | 推荐策略 |
|------|----------|
| `GenerateDailyRecommendAsync(userId, count=30)` | **每日推荐**：60% 常听艺术家其他歌曲 + 30% 同流派未听过歌曲 + 10% 完全随机探索；不足则随机填充 |
| `GetRecentlyPlayedAsync(userId, count=50)` | 最近播放（去重） |
| `GetTopPlayedAsync(userId, count=100, days=30)` | Top 播放（按天数窗口 GroupBy SongId 计数排序） |
| `GetDiscoverAsync(userId, count=30)` | 发现：听过 ≥3 次的视为"已听"，从未听过或极少播放的歌曲 |
| `GetArtistMixAsync(artistId, userId, count=30)` | 艺术家 mix：取该艺术家 Top 3 流派 → 同流派其他 5 个艺术家 → 混合打乱 |

所有方法返回 `List<long>`（SongId 列表），由 Controller 再加载详情。内部用 Fisher-Yates `Shuffle` 打乱。

### 7.8 StatsService — 播放统计

文件：[Services/StatsService.cs](file:///d:/Code/CatClawMusicServer/Services/StatsService.cs)

| 方法 | 职责 |
|------|------|
| `GetUserStatsAsync(userId, days=30)` | 个人统计：总播放数、总时长(小时)、Top10 艺术家/歌曲、每日播放数、24 小时分布、流派分布 |
| `GetServerStatsAsync()` | 全服统计（admin）：歌曲/艺术家/专辑/用户数、总播放数、7 天活跃用户 |
| `AggregateDailyStatsAsync()` | 预聚合当日统计到 `StatsDaily` 表（每用户每天一条，upsert） |

### 7.9 EventBus — WebSocket 事件总线

文件：[Services/EventBus.cs](file:///d:/Code/CatClawMusicServer/Services/EventBus.cs)

| 方法 | 职责 |
|------|------|
| `Subscribe(connectionId, socket)` | 注册 WebSocket 订阅者 |
| `Unsubscribe(connectionId)` | 移除订阅者 |
| `PublishAsync(eventType, data)` | 广播事件给所有在线订阅者，自动清理断开连接 |

事件类型常量（`EventTypes`）：`now_playing_changed` / `play_queue_updated` / `scan_progress` / `library_changed` / `favorite_changed` / `rating_changed`。

---

## 8. API 控制器层

### 8.1 V1 API（主力，JWT 鉴权）

路由前缀 `api/v1/`，全部 `[Authorize]`（除 AuthController 的公共端点），统一返回 `ApiResponse<T>`，HTTP 状态码始终 200，业务错误通过响应体 `code` 区分。

| 控制器 | 路由 | 主要职责 |
|--------|------|----------|
| [AuthController](file:///d:/Code/CatClawMusicServer/Controllers/V1/AuthController.cs) | `api/v1/auth` | 注册（首用户自动 admin）、登录（签发 access+refresh）、刷新、注销、`/me`、改密（吊销所有 refresh） |
| [AdminController](file:///d:/Code/CatClawMusicServer/Controllers/V1/AdminController.cs) | `api/v1/admin` | 扫描触发（全量/增量）、扫描状态、系统信息、用户管理、清理孤儿记录。内部二次校验 admin 角色 |
| [LibraryController](file:///d:/Code/CatClawMusicServer/Controllers/V1/LibraryController.cs) | `api/v1/library` | `/overview` 曲库总览（计数 + 最近 10 首） |
| [SongsController](file:///d:/Code/CatClawMusicServer/Controllers/V1/SongsController.cs) | `api/v1/songs` | 列表（多维筛选+排序+分页）、详情、流（原文件/FFmpeg 转码）、封面（多尺寸）、歌词（结构化）、下载 |
| [AlbumsController](file:///d:/Code/CatClawMusicServer/Controllers/V1/AlbumsController.cs) | `api/v1/albums` | 专辑 CRUD + 子歌曲 |
| [ArtistsController](file:///d:/Code/CatClawMusicServer/Controllers/V1/ArtistsController.cs) | `api/v1/artists` | 艺术家 CRUD + 子歌曲/专辑 |
| [PlaylistController](file:///d:/Code/CatClawMusicServer/Controllers/V1/PlaylistController.cs) | `api/v1/playlists` | 播放列表 CRUD + AddSongs + RemoveSong + Reorder（更新 SortOrder） |
| [PlayQueueController](file:///d:/Code/CatClawMusicServer/Controllers/V1/PlayQueueController.cs) | `api/v1/playqueue` | 播放队列（单用户单队列，SongIds≤500） |
| [FavoritesController](file:///d:/Code/CatClawMusicServer/Controllers/V1/FavoritesController.cs) | `api/v1/favorites` | 收藏 CRUD + `check` 批量检查 |
| [RatingsController](file:///d:/Code/CatClawMusicServer/Controllers/V1/RatingsController.cs) | `api/v1/ratings` | 评分 1-5 CRUD |
| [ScrobbleController](file:///d:/Code/CatClawMusicServer/Controllers/V1/ScrobbleController.cs) | `api/v1/scrobble` | Scrobble 写入 + `/api/v1/history` 历史 |
| [SearchController](file:///d:/Code/CatClawMusicServer/Controllers/V1/SearchController.cs) | `api/v1/search` | 三维度并发搜索（`Task.WhenAll`） |
| [GenreController](file:///d:/Code/CatClawMusicServer/Controllers/V1/GenreController.cs) | `api/v1/genres` | 流派统计 + 流派下歌曲分页 |
| [StatsController](file:///d:/Code/CatClawMusicServer/Controllers/V1/StatsController.cs) | `api/v1/stats` | 用户统计 + 服务器统计（admin） |
| [RecommendController](file:///d:/Code/CatClawMusicServer/Controllers/V1/RecommendController.cs) | `api/v1/recommend` | daily / recent / top / discover / artist-mix |
| [HlsController](file:///d:/Code/CatClawMusicServer/Controllers/V1/HlsController.cs) | `api/v1/hls` | HLS 转码流：master.m3u8 / {bitrate}/index.m3u8 / {segment} |

**V1 共性特征**：
- userId 从 JWT `sub` claim 解析
- 只读查询统一 `AsNoTracking()`，关联用 `Include`/`ThenInclude`
- 分页：page(≥1) + page_size(1-200 clamp) + total + total_pages
- 请求 DTO 用 `record` 类型

### 8.2 Subsonic 兼容层

路由前缀 `/rest`，采用 partial class 拆分 9 个文件，共享 [SubsonicBase.cs](file:///d:/Code/CatClawMusicServer/Controllers/Subsonic/SubsonicBase.cs)。

**认证**：通过查询参数 `u` + `p`/`s`+`t`（Subsonic 标准 token salt），实际鉴权由前置 `ApiAuthMiddleware` 完成。

**响应格式**：统一 `{ "subsonic-response": { "status": "ok"|"failed", "version": "1.16.1", "type": "CatClawMusicServer", "serverVersion": "2.0.0", ...payload } }`。

| 文件 | 实现端点 |
|------|----------|
| SubsonicBase.cs | 基类：`SubsonicOk` / `SubsonicError` 响应构建器 + DTO 转换器（`SongToDict`/`AlbumToDict`/`ArtistToDict`/`PlaylistToDict`/`MimeByExt`） |
| SubsonicSystem.cs | `ping.view` / `getLicense.view` / `getUser.view` / `getScanStatus.view` |
| SubsonicBrowsing.cs | `getMusicFolders.view` / `getIndexes.view` / `getArtists.view` / `getArtist.view` / `getMusicDirectory.view` / `getGenres.view` / `getSongsByGenre.view` |
| SubsonicAlbums.cs | `getAlbumList2.view` / `getAlbumList.view` / `getAlbum.view` / `getSong.view` / `getRandomSongs.view` / `getStarred2.view` / `getNowPlaying.view` |
| SubsonicMedia.cs | `stream.view`（Range 支持）/ `download.view` / `getCoverArt.view`（songId + albumId）/ `getLyricsBySongId.view` |
| SubsonicPlaylists.cs | `getPlaylists.view` / `getPlaylist.view` / `createPlaylist.view` / `updatePlaylist.view` / `deletePlaylist.view` |
| SubsonicAnnotations.cs | `star.view` / `unstar.view` / `setRating.view` / `scrobble.view` |
| SubsonicBookmarks.cs | `getPlayQueue.view` / `savePlayQueue.view` / `getBookmarks.view` |
| SubsonicSearch.cs | `search2.view` / `search3.view`（三维度并发） |

### 8.3 V0 旧版 API

路由前缀 `/api/*`，AccessToken 鉴权（Bearer 或 cookie），裸 JSON 响应。包含 [SongsController](file:///d:/Code/CatClawMusicServer/Controllers/SongsController.cs)、[ScanController](file:///d:/Code/CatClawMusicServer/Controllers/ScanController.cs) 等，向后兼容保留。

### 8.4 ClawCircle 调试控制器

文件：[Controllers/ClawCircleController.cs](file:///d:/Code/CatClawMusicServer/Controllers/ClawCircleController.cs)，路由 `api/clawcircle`，提供运维调试端点：

- `GET /peers` — 在线节点快照
- `GET /stats` — tracker + DHT + 信誉统计
- `GET /dht/nodes` — DHT 路由表节点
- `GET /reputation` — 节点信誉详情
- `GET /library/status` — 曲库索引发布状态
- `GET /find-song?artist=&title=` — DHT 查询歌曲持有者
- `POST /dht/toggle` — 启停 DHT
- `POST /dht/bootstrap` — 手动 Bootstrap

---

## 9. ClawCircle P2P 模块

猫爪驿站是跨网 P2P 音乐共享功能，服务端扮演 tracker / 信令中转 + DHT 节点 + STUN 反射角色。

### 9.1 架构层级

```
┌─────────────────────────────────────────────────────┐
│  客户端 A (CatClawMusic)        客户端 B             │
│  ┌─────────────────┐          ┌─────────────────┐  │
│  │ WebRTC P2P 直连  │◄────────►│ WebRTC P2P 直连  │  │
│  │ TransferEngine  │  分块传输 │ TransferEngine  │  │
│  └────────┬────────┘          └────────┬────────┘  │
│           │                            │            │
│           │   WebSocket 信令 (SDP/ICE) │            │
│           └────────────┬───────────────┘            │
└────────────────────────┼────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────┐
│              CatClawMusicServer (本仓库)             │
│  ┌──────────────────┐  ┌─────────────────────────┐ │
│  │ ClawCircleTracker│  │ ClawCircleWebSocketMW   │ │
│  │ 在线节点注册表    │◄─┤ /ws/clawcircle 信令中转  │ │
│  └──────────────────┘  └─────────────────────────┘ │
│  ┌──────────────────┐  ┌─────────────────────────┐ │
│  │ ClawCircleStun   │  │ DhtService (Kademlia)   │ │
│  │ UDP NAT 反射      │  │ UDP 37825 节点发现+KV   │ │
│  │ UDP 37824        │  │ BloomFilter 曲库索引    │ │
│  └──────────────────┘  └─────────────────────────┘ │
│  ┌──────────────────┐  ┌─────────────────────────┐ │
│  │ NodeReputation   │  │ TransferEngine          │ │
│  │ 节点信誉(0-100)   │  │ 分块传输协议参考实现     │ │
│  └──────────────────┘  └─────────────────────────┘ │
└─────────────────────────────────────────────────────┘
```

### 9.2 核心组件

#### ClawCircleTracker — 在线节点注册表

文件：[ClawCircle/ClawCircleTracker.cs](file:///d:/Code/CatClawMusicServer/ClawCircle/ClawCircleTracker.cs)

单例，`ConcurrentDictionary<string, PeerSession>` 维护 deviceId → 会话映射。每个 `PeerSession` 含独立 `SemaphoreSlim SendLock` 保证单 socket 串行发送。

关键方法：`Register` / `UpdateLibrary` / `SetUdpEndpoint` / `Remove` / `SendToAsync` / `BroadcastAsync` / `Snapshot` / `Find`。

#### ClawCircleWebSocketMiddleware — 信令中间件

文件：[ClawCircle/ClawCircleWebSocketMiddleware.cs](file:///d:/Code/CatClawMusicServer/ClawCircle/ClawCircleWebSocketMiddleware.cs)

拦截 `/ws/clawcircle`，鉴权（`?token=` 或 `Authorization: Bearer`，未配置 AccessToken 免鉴权）→ WebSocket 升级 → 消息分发。

**消息协议**（JSON，camelCase，含 `type` 字段分发）：

| 客户端→服务端 | 说明 |
|---------------|------|
| `register` | 宣告上线 + 曲库摘要（library） |
| `library_update` | 更新曲库摘要 |
| `query_peer` | 查询某节点信息 |
| `find_song` | 查询哪些节点拥有某歌（键 `artist\u0001title` 小写） |
| `signal` | 转发 WebRTC 信令（SDP/ICE） |
| `bye` | 主动下线 |

| 服务端→客户端 | 说明 |
|---------------|------|
| `welcome` | 握手成功 + 其他在线节点 + 服务器时间 |
| `peer_online` / `peer_offline` / `peer_update` | 节点上下线/曲库变更广播 |
| `peer_info` / `song_holders` | 查询答复 |
| `relay` | 转发的信令 |
| `error` | 错误 |

#### ClawCircleStunService — STUN NAT 反射

文件：[ClawCircle/ClawCircleStunService.cs](file:///d:/Code/CatClawMusicServer/ClawCircle/ClawCircleStunService.cs)

UDP 端口（默认 37824 = HTTP + 1）接收节点探测包，观察 NAT 映射后的公网 IP:端口，写入 `tracker.SetUdpEndpoint`，并回包告知节点自身反射端点。IPv6 双栈绑定优先，端口冲突自动 +1 重试。

#### NodeReputation — 节点信誉

文件：[ClawCircle/NodeReputation.cs](file:///d:/Code/CatClawMusicServer/ClawCircle/NodeReputation.cs)

信誉值 0-100（初始 50）。`RecordSuccess(+2)` / `RecordFailure(-5)` / `RecordTimeout(-3)`。阈值：`BlacklistThreshold=10`、`TrustedThreshold=80`。`ConcurrentDictionary` 线程安全，30 天未见记录自动清理。

#### BloomFilter — 布隆过滤器

文件：[ClawCircle/BloomFilter.cs](file:///d:/Code/CatClawMusicServer/ClawCircle/BloomFilter.cs)

压缩曲库索引用于高效广播。最优大小 `m = -n*ln(p)/(ln2)²`，最优哈希数 `k = m/n*ln2`。SHA256 变体哈希，误判率默认 1%。支持 `Export`/`Import` Base64 序列化便于 DHT 发布。

### 9.3 DHT（Kademlia）子系统

#### NodeId — 160 位节点 ID

文件：[ClawCircle/Dht/NodeId.cs](file:///d:/Code/CatClawMusicServer/ClawCircle/Dht/NodeId.cs)

`readonly struct`，20 字节，基于 SHA-1。`FromString`(SHA-1 哈希) / `FromHex` / `Random`(密码学随机)。核心方法 `XorDistance(other)`（XOR 距离度量）和 `BucketIndex(other)`（0=自身，159=最远）。

#### KBucket — K-Bucket 路由表

文件：[ClawCircle/Dht/KBucket.cs](file:///d:/Code/CatClawMusicServer/ClawCircle/Dht/KBucket.cs)

`RoutingTable` 按 XOR 距离将节点分入 160 个桶。常量 `K=20`（每桶最大）、`Alpha=3`（并发查询）。`KBucket` 实现 LRU 淘汰：桶满时检查最旧节点是否超过 15 分钟未见，是则淘汰否则拒绝。

`RoutingTable` 关键方法：`AddOrUpdate` / `Remove` / `FindClosest(target, count=K)`。

`DhtNode` 模型：Id + Endpoint + LastSeen + Reputation(默认 50) + `IsAlive`(30 分钟内见过)。

#### DhtService — Kademlia DHT 服务

文件：[ClawCircle/Dht/DhtService.cs](file:///d:/Code/CatClawMusicServer/ClawCircle/Dht/DhtService.cs)

实现 Kademlia 4 种 RPC：**PING / FIND_NODE / FIND_VALUE / STORE**。

| 方法 | 职责 |
|------|------|
| `Start()` | UDP 绑定（IPv6 双栈 + IPv4 回退，默认 37825）+ 接收循环 + 维护循环 |
| `BootstrapAsync(endpoint)` / `BootstrapFromAddressAsync(string)` | 从引导节点加入网络（支持域名解析），PING 后 `IterativeFindNodeAsync(自身)` 填充路由表 |
| `IterativeFindNodeAsync(target)` | 迭代查找距离目标最近的 K 个节点（按 XOR 距离排序） |
| `StoreAsync(key, value)` | 本地存储 + 向距离 key 最近的 α 个节点发送 STORE |
| `FindValueAsync(key)` | 先查本地，再迭代向最近节点发 FIND_VALUE |
| `PublishLibraryIndexAsync()` | 从 DB 读取所有歌曲键（`artist\u0001title` 小写），构建 BloomFilter，发布到 DHT（key = `library:{NodeId}`），每 30 分钟重新发布 |
| `FindSongHoldersAsync(songKey)` | 遍历所有已知节点的 `library:*` 键，下载 BloomFilter 检查是否包含该歌 |

**RPC 机制**：每个请求带 `Id`(Guid 前 16 位)，响应回 `RpcId` 匹配。`_pendingRpcs` 用 `ConcurrentDictionary<string, TaskCompletionSource<DhtRpcResponse>>` 管理，5 秒超时。接收循环对每个 UDP 包 `Task.Run` 并发处理。

**维护循环**：启动延迟 10 秒等 bootstrap；每 5 分钟刷新路由表（随机 NodeId FIND_NODE）；每 30 分钟重新发布曲库索引。

辅助：`ConcurrentKeyValueStore`（基于 `ConcurrentDictionary` + TTL 1 小时）、`LibraryIndexPayload`、`SongHolder`、`DhtOptions`。

### 9.4 TransferEngine — 分块传输引擎

文件：[ClawCircle/Transfer/TransferEngine.cs](file:///d:/Code/CatClawMusicServer/ClawCircle/Transfer/TransferEngine.cs)

P2P 文件分块传输协议参考实现。`ChunkSize = 256KB`。

| 方法 | 职责 |
|------|------|
| `CreateManifestAsync(filePath)` | 发送方：文件分块 + 每块 SHA256 → `PieceManifest` |
| `CreateReceiveTask(manifest, outputDir)` | 接收方：创建 `TransferTask`（含 `bool[] ReceivedChunks` 跟踪） |
| `ReceiveChunk(taskId, chunkIndex, data)` | 接收一块：SHA256 校验 → 按 offset 写入文件 → 更新状态；全部完成置 `Complete` |
| `GetMissingChunks(taskId)` | 返回缺失块索引（断点续传） |
| `ReadChunkAsync(filePath, chunkIndex)` | 发送方读取指定块 |

数据结构：`TransferStatus`（Created/InProgress/Complete/Failed）、`TransferTask`、`PieceManifest`、`ChunkInfo { Index, Size, Sha256 }`。

---

## 10. 中间件与鉴权体系

中间件管道顺序（[Program.cs](file:///d:/Code/CatClawMusicServer/Program.cs) 第 217-236 行）：

```
Swagger(Dev) → CORS → WebSockets
  → ClawCircleWebSocketMiddleware   # /ws/clawcircle 信令
  → ApiAuthMiddleware               # /api 与 /rest 鉴权
  → WebUiAuthMiddleware             # Web UI 静态页鉴权
  → DefaultFiles → StaticFiles
  → RateLimiter                     # 每 IP 200 req/min
  → Authentication (JWT)            # /api/v1/* 路由
  → Authorization
  → MapControllers
```

### 10.1 ApiAuthMiddleware — 内网鉴权

文件：[ApiAuthMiddleware.cs](file:///d:/Code/CatClawMusicServer/ApiAuthMiddleware.cs)

保护 `/api` 与 `/rest` 端点。**若未配置 AccessToken，全部放行**（本地开发友好）。

| 端点 | 鉴权方式 |
|------|----------|
| `/api/auth/*`、`/api/v1/auth/*`、`/api/config`、`/api/scan/status` | 公共端点，放行 |
| `/api/v1/*` | 交由 ASP.NET Core JWT Bearer 中间件处理，放行 |
| `/rest/*` | Subsonic token 认证：`t == md5(AccessToken + s)` |
| `/api/*`（V0） | Bearer token（先查静态 AccessToken，再查 JWT）或 cookie `catclaw_session == AccessToken` |

### 10.2 WebUiAuthMiddleware — Web UI 鉴权

文件：[WebUiAuthMiddleware.cs](file:///d:/Code/CatClawMusicServer/WebUiAuthMiddleware.cs)

保护静态 HTML 页面。

- **数据库已有用户** → JWT 认证由 Vue SPA 处理，全部放行
- **无用户且管理员未配置** → 引导 `/register.html`
- **已注册** → 检查 cookie `catclaw_session == AccessToken`，未登录跳 `/login.html`
- `/api/`、`/rest/`、`/ws/`、`/swagger`、`/login.html` 始终放行

### 10.3 AdminCredentialStore — 管理员凭据

文件：[AdminCredentialStore.cs](file:///d:/Code/CatClawMusicServer/AdminCredentialStore.cs)

- 首次启动从 `appsettings.json` 读取默认值，`AdminPassword` 为空视为「未配置」
- 注册后写入 `Data/admin.json`，后续重启优先读文件
- `TrySetCredentialsAsync` 仅未配置时允许设置，已注册后拒绝覆盖
- `claw reset` 命令删除 `admin.json` 重置

### 10.4 EventWebSocketMiddleware — 事件推送

文件：[EventWebSocketMiddleware.cs](file:///d:/Code/CatClawMusicServer/EventWebSocketMiddleware.cs)

拦截 `/ws/events`，升级为 WebSocket 并注册到 `EventBus`。客户端连接后自动接收全局事件推送，支持 ping/pong 心跳。

---

## 11. Web UI 前端

目录：[wwwroot/](file:///d:/Code/CatClawMusicServer/wwwroot/)

| 文件 | 职责 |
|------|------|
| `index.html` | Vue 3 SPA 入口（ESM import map 加载 Vue + Vue Router + HLS.js） |
| `login.html` / `register.html` | 登录 / 注册页 |
| `css/style.css` | 样式 |
| `js/api.js` | API 封装（fetch + JWT 拦截） |
| `js/app.js` | Vue 应用主入口 |
| `js/player.js` | 播放器逻辑（HLS.js 自适应流） |
| `js/store.js` | 状态管理 |

前端通过 JWT 与 V1 API 交互，HLS.js 处理 `/api/v1/hls/*` 自适应流媒体。

---

## 12. 部署与运行方式

### 12.1 一键部署（推荐）

```bash
# 国内 Gitee 镜像
curl -fsSL https://gitee.com/kankejiang/catclaw-server/raw/master/install_gitee.sh | sudo bash

# GitHub（海外）
curl -fsSL https://raw.githubusercontent.com/kankejiang/catclaw-server/master/install.sh | sudo bash
```

脚本：[install.sh](file:///d:/Code/CatClawMusicServer/install.sh) / [install_gitee.sh](file:///d:/Code/CatClawMusicServer/install_gitee.sh)。自动安装 Docker、克隆源码、构建镜像、交互式配置端口/令牌/音乐目录。已安装则切换增量更新模式（保留配置、数据库、管理员账号）。

### 12.2 Docker 部署

[Dockerfile](file:///d:/Code/CatClawMusicServer/Dockerfile)：多阶段构建（SDK 编译 → aspnet 运行时 + FFmpeg）。

[docker-compose.yml](file:///d:/Code/CatClawMusicServer/docker-compose.yml)：

```yaml
services:
  catclaw-server:
    ports:
      - "37823:37823"       # HTTP / WebSocket / Web UI
      - "37824:37824/udp"   # STUN（NAT 打洞）
      - "37825:37825/udp"   # DHT（节点发现）
    volumes:
      - /mnt/media/music:/music
      - ./catclaw-data:/data
```

**端口约定**：
- `37823` — HTTP / WebSocket / Web UI
- `37824/udp` — STUN NAT 反射（HTTP 端口 + 1）
- `37825/udp` — DHT 节点发现

**音量**：`/music`（音乐库，只读）+ `/data`（数据库、封面、转码缓存，持久化）

### 12.3 Windows 自包含 EXE

```bash
dotnet publish CatClawMusicServer.csproj -c Release -r win-x64 --self-contained -o publish
# 产物：publish/CatClawMusicServer.exe（约 110MB，捆绑 .NET 8 运行时）

# 运行示例
CatClawMusicServer.exe --music-dir "D:\Music" --token "强随机令牌" --port 37823
CatClawMusicServer.exe --music-dir "D:\Music" --scan-and-exit   # 仅建库
CatClawMusicServer.exe claw reset                                # 重置管理员
```

### 12.4 开发运行

```bash
dotnet run    # 默认 http://localhost:37823，Development 环境启用 Swagger
```

[launchSettings.json](file:///d:/Code/CatClawMusicServer/Properties/launchSettings.json) 配置开发环境。

### 12.5 GitHub Actions 自动构建

[.github/workflows/docker-publish.yml](file:///d:/Code/CatClawMusicServer/.github/workflows/docker-publish.yml)：推送到 `main`/`master` 或打 `v*` tag 自动构建并推送到 GHCR (`ghcr.io/kankejiang/catclaw-server`)，使用 GHA 缓存加速。

### 12.6 客户端连接

CatClawMusic 手机端：「远程音乐」→ 协议选 **Navidrome**，地址填 `http://<NAS内网IP>:37823`，用户名任意，密码填 `--token` 的值。

### 12.7 管理员重置

```bash
docker exec -it catclaw-server dotnet CatClawMusicServer.dll claw reset
docker compose -f /opt/catclaw-server/docker-compose.yml restart
```

---

## 13. 关键业务流程

### 13.1 音乐库扫描流程

```
触发：POST /api/v1/admin/scan (admin) | CLI --scan-and-exit | POST /api/scan (V0)
  ↓
MusicScanner.ScanDirectoryAsync / IncrementalScanAsync
  ↓
Directory.GetFiles(递归) → 过滤 14 种扩展名
  ↓
逐文件：
  ├─ ComputeFileHash (增量扫描时：哈希匹配则跳过)
  ├─ FileTagService.ReadTags → TagLibSharp 读标签 + 提取封面
  ├─ FileTagService.FindLyricsFile / FindCoverFile
  ├─ EnsureArtistAsync (不存在则创建)
  ├─ EnsureAlbumAsync (不存在则创建)
  └─ 新增 Song 或更新已存在 Song (按 FilePath 匹配)
  ↓
每 100 文件 SaveChangesAsync → 最终 SaveChangesAsync
  ↓
返回 ScanResult { Processed, Added, Updated, Skipped, Errors }
```

### 13.2 流媒体播放流程

```
客户端请求播放
  ↓
┌─ 原文件流 (V1 /api/v1/songs/{id}/stream?format=original)
│    └─ PhysicalFile + enableRangeProcessing (HTTP 206 Range)
│
├─ 转码直出 (V1 /api/v1/songs/{id}/stream?format=opus|mp3|aac)
│    └─ TranscodingService.GetOrStartStreamJobAsync
│       → FFmpeg -c:a libopus/libmp3lame/aac pipe:1
│       → process.StandardOutput.CopyTo(Response.Body)
│
└─ HLS 自适应 (V1 /api/v1/hls/{songId}/master.m3u8)
     ├─ BuildMasterPlaylist (多码率: 96/160/256 + original)
     ├─ GET /{songId}/{bitrate}/index.m3u8
     │    ├─ bitrate=0/original → BuildOriginalPlaylist (无损直出)
     │    └─ GetOrStartHlsJobAsync → FFmpeg -f hls 分片
     │       → WaitForSegmentsAsync(3) 等首批分片
     │       → 替换分片路径为相对 URL
     └─ GET /{songId}/{bitrate}/{segment} → 返回 .aac 分片
```

### 13.3 认证流程

```
注册 (POST /api/v1/auth/register)
  ├─ 首用户 → 自动 admin 角色
  └─ 后续用户 → 需管理员 Bearer token
  ↓
BCrypt 哈希密码 → 写入 Users 表
  ↓
登录 (POST /api/v1/auth/login)
  ↓
BCrypt 验证 → JwtService.GenerateAccessToken (15min)
           → JwtService.GenerateRefreshToken (30d)
  ↓
写入 RefreshTokens 表 + 更新 LastLoginAt + 注册/更新 Device
  ↓
返回 { access_token, refresh_token, user }
  ↓
后续请求：Authorization: Bearer <access_token>
  ↓
Access Token 过期 → POST /api/v1/auth/refresh
  ├─ 校验 refresh_token 未吊销未过期
  ├─ 吊销旧 token
  └─ 签发新 token 对
```

### 13.4 P2P 跨网共享流程

```
节点 A 上线
  ↓
WebSocket 连接 ws://host:37823/ws/clawcircle?token=<AccessToken>
  ↓
发送 register { deviceId, name, library(曲库摘要) }
  ↓
服务端：ClawCircleTracker.Register
  ↓
回复 welcome { you, peers(其他在线节点), serverTime }
  ↓
广播 peer_online 给其他节点
  ↓
节点 A 向 STUN (UDP 37824) 探测 → 获取反射端点 (wan, port)
  ↓
DHT：PublishLibraryIndexAsync → BloomFilter 曲库索引发布到 library:{NodeId}
  ↓
节点 B 想找歌 "artist - title"
  ├─ 方式1：find_song (tracker 内存匹配在线节点 Library.SongKeys)
  └─ 方式2：DhtService.FindSongHoldersAsync (遍历 library:* BloomFilter)
  ↓
返回 song_holders { songKey, holders[{deviceId, wan, port}] }
  ↓
节点 B → 节点 A 发送 signal (SDP offer)
  ↓
服务端转发为 relay 给 A → A 回 signal (SDP answer) → 转发给 B
  ↓
ICE 候选交换完成 → WebRTC P2P 直连建立
  ↓
TransferEngine：CreateManifest → 分块传输 (256KB/块, SHA256 校验, 断点续传)
  ↓
NodeReputation：成功 +2 / 失败 -5 / 超时 -3
```

---

> 本 Wiki 基于源码静态分析生成，反映仓库当前状态。如需了解特定端点的精确参数与返回结构，请参阅 [Swagger UI](http://localhost:37823/swagger)（Development 环境）或对应控制器源码。
