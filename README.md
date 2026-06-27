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

访问 `http://localhost:5000` 查看 Web UI
访问 `http://localhost:5000/swagger` 查看 API 文档

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
