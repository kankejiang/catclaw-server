#!/usr/bin/env bash
# ============================================================
#  CatClaw Server — Docker 一键安装（克隆源码 + 构建）
#  用法:  curl -fsSL https://raw.githubusercontent.com/kankejiang/catclaw-server/master/install.sh | bash
#        MUSIC_DIR=/vol1/music bash install.sh
# ============================================================

set -e

INSTALL_DIR="${INSTALL_DIR:-/opt/catclaw}"
MUSIC_DIR="${MUSIC_DIR:-}"
HTTP_PORT="${HTTP_PORT:-66880}"
DHT_PORT="${DHT_PORT:-66881}"
DEVICE_NAME="${DEVICE_NAME:-$(hostname 2>/dev/null || echo 'CatClaw')}"
RATE_LIMIT="${RATE_LIMIT:-128}"
BOOTSTRAP_NODES="${BOOTSTRAP_NODES:-music.08102516.xyz:6881}"
REPO_URL="${REPO_URL:-https://github.com/kankejiang/catclaw-server.git}"
GIT_BRANCH="${GIT_BRANCH:-master}"

RED='\033[0;31m'; GREEN='\033[0;32m'; CYAN='\033[0;36m'; NC='\033[0m'
log()  { echo -e "${GREEN}[✓]${NC} $*"; }
warn() { echo -e "${RED}[✗]${NC} $*"; }
info() { echo -e "${CYAN}[i]${NC} $*"; }

echo "╔════════════════════════════════════╗"
echo "║   🐾 CatClaw Server 一键安装      ║"
echo "╚════════════════════════════════════╝"
echo ""

# ── 检查 Docker ─────────────────────────────────────────────
if ! command -v docker &>/dev/null; then
    warn "未找到 Docker"
    info "飞牛OS: 在应用中心安装 Docker"
    info "其他:   curl -fsSL https://get.docker.com | bash"
    exit 1
fi

# ── 音乐目录 ────────────────────────────────────────────────
if [ -z "$MUSIC_DIR" ]; then
    for dir in "/vol1/music" "/volume1/music" "/mnt/music" "/mnt/nas/music" "/media/music" "$HOME/music" "$HOME/Music"; do
        [ -d "$dir" ] && { MUSIC_DIR="$dir"; break; }
    done
    if [ -z "$MUSIC_DIR" ]; then
        info "未检测到音乐目录，使用默认路径 /vol1/music"
        info "安装后可在 Web UI 中修改"
        MUSIC_DIR="/vol1/music"
    fi
fi
[ ! -d "$MUSIC_DIR" ] && mkdir -p "$MUSIC_DIR" 2>/dev/null || true

# ── 创建安装目录 ────────────────────────────────────────────
mkdir -p "$INSTALL_DIR/build_cache"

info "安装目录: $INSTALL_DIR"
info "音乐目录: $MUSIC_DIR"
info "HTTP 端口: $HTTP_PORT"
info "DHT 端口:  $DHT_PORT"
info "设备名称:  $DEVICE_NAME"
echo ""

# ── 克隆源码 ────────────────────────────────────────────────
WORK_DIR="$INSTALL_DIR/src"
if [ -d "$WORK_DIR/.git" ]; then
    info "更新已有源码..."
    cd "$WORK_DIR"
    git fetch origin "$GIT_BRANCH" 2>/dev/null || true
    git checkout "$GIT_BRANCH" 2>/dev/null || true
    git pull origin "$GIT_BRANCH" 2>/dev/null || true
else
    info "克隆源码..."
    rm -rf "$WORK_DIR"
    if command -v git &>/dev/null; then
        git clone --depth 1 --branch "$GIT_BRANCH" "$REPO_URL" "$WORK_DIR"
    else
        warn "未安装 git，尝试下载 tar.gz..."
        TARBALL="$INSTALL_DIR/source.tar.gz"
        wget -O "$TARBALL" "https://github.com/kankejiang/catclaw-server/archive/refs/heads/${GIT_BRANCH}.tar.gz" 2>/dev/null \
            || curl -L -o "$TARBALL" "https://github.com/kankejiang/catclaw-server/archive/refs/heads/${GIT_BRANCH}.tar.gz" \
            || { warn "下载失败"; exit 1; }
        tar xzf "$TARBALL" -C "$INSTALL_DIR"
        rm -f "$TARBALL"
        # tar extracts to catclaw-server-master/
        mv "$INSTALL_DIR/catclaw-server-${GIT_BRANCH}" "$WORK_DIR" 2>/dev/null || true
    fi
fi
log "源码就绪: $WORK_DIR"

# ── 构建并启动 ──────────────────────────────────────────────
cd "$WORK_DIR"

# Copy go module cache from previous build (speed up rebuilds)
if [ -d "$INSTALL_DIR/build_cache/gomod" ]; then
    mkdir -p "$WORK_DIR/go-mod-cache"
    cp -r "$INSTALL_DIR/build_cache/gomod"/* "$WORK_DIR/go-mod-cache/" 2>/dev/null || true
fi

info "构建 Docker 镜像 (首次约 2-3 分钟)..."
docker build -t catclaw-server:latest "$WORK_DIR"

# ── 启动容器 ────────────────────────────────────────────────
info "启动容器..."

# 停止并删除旧容器
docker rm -f catclaw-server 2>/dev/null || true

mkdir -p "$INSTALL_DIR/data"

docker run -d \
    --name catclaw-server \
    --restart unless-stopped \
    -p "$HTTP_PORT:66880" \
    -p "$DHT_PORT:66881/udp" \
    -v "$MUSIC_DIR:/music:ro" \
    -v "$INSTALL_DIR/data:/data" \
    -e BOOTSTRAP_NODES="$BOOTSTRAP_NODES" \
    -e RATE_LIMIT="$RATE_LIMIT" \
    -e DEVICE_NAME="$DEVICE_NAME" \
    catclaw-server:latest

sleep 2
IP=$(hostname -I 2>/dev/null | awk '{print $1}' || echo 'localhost')

echo ""
log "部署完成！"
echo ""
info "Web UI:  http://${IP}:${HTTP_PORT}"
info "查看日志: docker logs -f catclaw-server"
info "重启服务: docker restart catclaw-server"
info ""
info "更新到最新版:"
info "  bash $INSTALL_DIR/install.sh"
