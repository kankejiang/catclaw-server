# 🐾 CatClaw Server

**自建 P2P 音乐流媒体服务器** — 扫描本地音乐库，通过 HTTP API 流式播放，使用 Kademlia DHT 发现局域网/广域网内其他设备。

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Go 1.22+](https://img.shields.io/badge/Go-1.22+-00ADD8.svg)](https://go.dev/)

---

## ✨ 功能

- 🎵 **音乐库扫描** — 自动扫描目录，读取 MP3/FLAC/WAV/OGG 等 14 种格式的 ID3/FLAC 标签
- 🌐 **HTTP API** — RESTful 接口：歌曲列表、搜索、流媒体播放（支持 Range 断点续传）
- 📡 **P2P 设备发现** — Kademlia DHT（160-bit NodeID，k-bucket 路由表）发现局域网内其他设备
- ⚡ **速率限制** — 令牌桶限速，可通过 API 动态调整
- 🖥️ **Web UI** — 内嵌管理界面：状态监控、限速控制、设备列表、一键扫描
- 🧹 **零配置** — 单二进制文件，SQLite 存储，Docker 一键部署

---

## 📦 一鍵部署（Docker）

```bash
# 下载部署脚本（二选一）
curl -O https://raw.githubusercontent.com/kankejiang/catclaw-server/master/deploy.sh
# 或
wget https://raw.githubusercontent.com/kankejiang/catclaw-server/master/deploy.sh

# 编辑配置后运行
vim deploy.sh   # 修改 MUSIC_DIR 为你的音乐目录
bash deploy.sh
```

---

## 🚀 手动运行

### 下载二进制

从 [GitHub Releases](https://github.com/kankejiang/catclaw-server/releases) 下载对应平台的二进制，直接运行：

```bash
./catclaw-server \
  --music-dir=/path/to/music \
  --http-port=66880 \
  --dht-port=66881 \
  --device-name=MyNAS
```

### Docker Compose

```yaml
services:
  catclaw-server:
    image: ghcr.io/kankejiang/catclaw-server:latest
    container_name: catclaw-server
    restart: unless-stopped
    ports:
      - "66880:66880"
      - "66881:66881/udp"
    volumes:
      - /path/to/music:/music:ro
      - /path/to/data:/data
    environment:
      - DEVICE_NAME=MyNAS
      - RATE_LIMIT=128
      - BOOTSTRAP_NODES=music.08102516.xyz:6881
```

```bash
docker compose up -d
```

### 从源码编译

```bash
git clone https://github.com/kankejiang/catclaw-server.git
cd catclaw-server
go build -o catclaw-server .
./catclaw-server --music-dir=/path/to/music
```

---

## ⚙️ 命令行参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `--http-port` | `66880` | HTTP API 和 Web UI 端口 |
| `--dht-port` | `66881` | DHT P2P UDP 端口 |
| `--music-dir` | `/music` | 音乐文件目录 |
| `--db-path` | `/data/catclaw.db` | SQLite 数据库路径 |
| `--bootstrap` | `music.08102516.xyz:6881` | DHT 引导节点 |
| `--rate-limit` | `128` | 流媒体限速 (KB/s)，0 = 不限速 |
| `--device-name` | (主机名) | 设备显示名称 |

---

## 📡 API 接口

| 方法 | 路径 | 说明 |
|------|------|------|
| `GET` | `/api/status` | 服务器状态 |
| `GET` | `/api/songs` | 歌曲列表 |
| `GET` | `/api/songs/{id}` | 歌曲详情 |
| `GET` | `/api/songs/{id}/cover` | 封面图片 |
| `GET` | `/api/stream/{id}` | 流媒体播放（支持 Range） |
| `GET` | `/api/search?q=` | 搜索歌曲 |
| `GET` | `/api/artists` | 艺术家列表 |
| `GET` | `/api/albums` | 专辑列表 |
| `POST` | `/api/scan` | 触发扫描 |
| `GET` | `/api/dht/devices` | P2P 发现的设备 |
| `GET` | `/api/dht/contacts` | DHT 路由表 |
| `GET/PUT` | `/api/config/ratelimit` | 读取/修改限速 |

---

## 🏗️ 项目结构

```
catclaw-server/
├── main.go                    # 入口：组装各模块
├── internal/
│   ├── api/router.go          # HTTP 路由及处理器
│   ├── db/database.go         # SQLite 数据库层
│   ├── dht/node.go            # Kademlia DHT 节点
│   ├── dht/routing.go         # DHT 路由表
│   ├── scanner/scanner.go     # 音乐目录扫描
│   └── limiter/rate_limiter.go # 令牌桶限速
├── web/index.html             # Web UI
├── Dockerfile
├── docker-compose.yml
└── deploy.sh                  # 一键部署脚本
```

---

## 🔗 原理

- **扫描** → `dhowden/tag` 读取标签 → 写入 SQLite
- **流媒体** → HTTP Range 分段传输 + 令牌桶限速
- **P2P** → Kademlia DHT 协议：Ping/Pong、FindNode、Store、FindValue
- **设备发现** → 通过 `device:` 前缀键在 DHT 中注册，其他节点可查询

---

## 📄 License

MIT © CatClaw Server Contributors
