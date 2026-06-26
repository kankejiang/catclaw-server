#!/usr/bin/env bash
# ============================================================
#  CatClaw Server — 一键部署脚本
#  支持: Docker / 直接运行二进制
#  License: MIT
# ============================================================

set -euo pipefail

# ── 配置（按需修改）────────────────────────────────────────
MUSIC_DIR="${MUSIC_DIR:-/path/to/music}"      # 修改为你的音乐目录
DATA_DIR="${DATA_DIR:-$HOME/.catclaw}"         # 数据库持久化目录
HTTP_PORT="${HTTP_PORT:-66880}"                # HTTP 端口
DHT_PORT="${DHT_PORT:-66881}"                  # DHT P2P 端口
DEVICE_NAME="${DEVICE_NAME:-$(hostname)}"      # 设备名
RATE_LIMIT="${RATE_LIMIT:-128}"                # 流媒体限速 KB/s
BOOTSTRAP_NODES="${BOOTSTRAP_NODES:-music.08102516.xyz:6881}"
DEPLOY_MODE="${DEPLOY_MODE:-docker}"           # docker | binary
VERSION="${VERSION:-latest}"                   # 版本号或 latest
# ────────────────────────────────────────────────────────────

RED='\033[0;31m'; GREEN='\033[0;32m'; CYAN='\033[0;36m'; NC='\033[0m'
log()  { echo -e "${GREEN}[✓]${NC} $*"; }
warn() { echo -e "${RED}[✗]${NC} $*"; }
info() { echo -e "${CYAN}[i]${NC} $*"; }

cat << 'EOF'
  🐾 CatClaw Server 部署脚本
─────────────────────────────────
EOF

# ── 检查 ────────────────────────────────────────────────────
if [ "$MUSIC_DIR" = "/path/to/music" ]; then
    warn "请先设置 MUSIC_DIR 为你的音乐目录"
    info "用法: MUSIC_DIR=/your/music bash deploy.sh"
    exit 1
fi

if [ ! -d "$MUSIC_DIR" ]; then
    warn "音乐目录不存在: $MUSIC_DIR"
    exit 1
fi

mkdir -p "$DATA_DIR"

# ── Docker 模式 ─────────────────────────────────────────────
deploy_docker() {
    if ! command -v docker &>/dev/null; then
        warn "未找到 Docker，请先安装: https://docs.docker.com/engine/install/"
        exit 1
    fi

    info "使用 Docker 模式部署"
    info "  音乐目录: $MUSIC_DIR"
    info "  数据目录: $DATA_DIR"
    info "  HTTP 端口: $HTTP_PORT"
    info "  DHT 端口:  $DHT_PORT"
    info "  设备名称:  $DEVICE_NAME"
    info "  限速:      ${RATE_LIMIT} KB/s"

    docker run -d \
        --name catclaw-server \
        --restart unless-stopped \
        -p "$HTTP_PORT:66880" \
        -p "$DHT_PORT:66881/udp" \
        -v "$MUSIC_DIR:/music:ro" \
        -v "$DATA_DIR:/data" \
        -e DEVICE_NAME="$DEVICE_NAME" \
        -e RATE_LIMIT="$RATE_LIMIT" \
        -e BOOTSTRAP_NODES="$BOOTSTRAP_NODES" \
        ghcr.io/kankejiang/catclaw-server:"$VERSION"

    log "Docker 容器已启动！"
    info "访问 http://$(hostname -I 2>/dev/null | awk '{print $1}' || echo 'localhost'):$HTTP_PORT"
}

# ── 二进制模式 ──────────────────────────────────────────────
deploy_binary() {
    ARCH=$(uname -m)
    case "$ARCH" in
        x86_64)  ARCH="amd64" ;;
        aarch64) ARCH="arm64" ;;
        armv7l)  ARCH="armv7" ;;
        *) warn "不支持的架构: $ARCH"; exit 1 ;;
    esac

    OS=$(uname -s | tr '[:upper:]' '[:lower:]')
    BINARY="catclaw-server-${OS}-${ARCH}"
    URL="https://github.com/kankejiang/catclaw-server/releases/download/v${VERSION}/${BINARY}"

    info "下载 $BINARY ..."
    curl -L -o "$DATA_DIR/catclaw-server" "$URL" 2>/dev/null || {
        warn "下载失败，请检查版本号或网络"
        exit 1
    }
    chmod +x "$DATA_DIR/catclaw-server"

    info "启动服务..."
    "$DATA_DIR/catclaw-server" \
        --music-dir="$MUSIC_DIR" \
        --db-path="$DATA_DIR/catclaw.db" \
        --http-port="$HTTP_PORT" \
        --dht-port="$DHT_PORT" \
        --bootstrap="$BOOTSTRAP_NODES" \
        --rate-limit="$RATE_LIMIT" \
        --device-name="$DEVICE_NAME"
}

# ── Systemd 服务 ────────────────────────────────────────────
install_systemd() {
    SERVICE_FILE="/etc/systemd/system/catclaw-server.service"
    info "安装 systemd 服务到 $SERVICE_FILE"

    cat > "$SERVICE_FILE" << SERVICE_EOF
[Unit]
Description=CatClaw Music Server
After=network.target

[Service]
Type=simple
ExecStart=$DATA_DIR/catclaw-server \\
    --music-dir=$MUSIC_DIR \\
    --db-path=$DATA_DIR/catclaw.db \\
    --http-port=$HTTP_PORT \\
    --dht-port=$DHT_PORT \\
    --bootstrap=$BOOTSTRAP_NODES \\
    --rate-limit=$RATE_LIMIT \\
    --device-name=$DEVICE_NAME
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
SERVICE_EOF

    systemctl daemon-reload
    systemctl enable catclaw-server
    systemctl start catclaw-server

    log "systemd 服务已安装并启动"
    info "systemctl status catclaw-server  查看状态"
    info "systemctl stop catclaw-server    停止服务"
}

# ── 主流程 ──────────────────────────────────────────────────
case "${1:-}" in
    docker|binary) DEPLOY_MODE="$1" ;;
    systemd)
        deploy_binary &>/dev/null &
        sleep 2
        install_systemd
        exit 0
        ;;
    help|-h|--help)
        echo "用法: bash deploy.sh [docker|binary|systemd]"
        echo "  docker   - Docker 容器部署（默认）"
        echo "  binary   - 下载二进制直接运行"
        echo "  systemd  - 二进制 + systemd 服务（推荐服务器）"
        exit 0
        ;;
esac

case "$DEPLOY_MODE" in
    docker) deploy_docker ;;
    binary) deploy_binary ;;
esac
