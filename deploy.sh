#!/usr/bin/env bash
# ============================================================
#  CatClaw Server — 一键部署脚本
#  用法:  bash deploy.sh              (交互模式，推荐)
#        MUSIC_DIR=/music bash deploy.sh
#        bash deploy.sh docker|binary|systemd|build
#  License: MIT
# ============================================================

set -euo pipefail

# ── 颜色 ────────────────────────────────────────────────────
RED='\033[0;31m'; GREEN='\033[0;32m'; CYAN='\033[0;36m'; YELLOW='\033[1;33m'; NC='\033[0m'
log()  { echo -e "${GREEN}[✓]${NC} $*"; }
warn() { echo -e "${RED}[✗]${NC} $*"; }
info() { echo -e "${CYAN}[i]${NC} $*"; }
ask()  { echo -e "${YELLOW}[?]${NC} $*"; }

cat << 'BANNER'
╔════════════════════════════════════╗
║     🐾 CatClaw Server 部署脚本     ║
╚════════════════════════════════════╝
BANNER

# ── 配置 ────────────────────────────────────────────────────
MUSIC_DIR="${MUSIC_DIR:-}"
DATA_DIR="${DATA_DIR:-$HOME/.catclaw}"
HTTP_PORT="${HTTP_PORT:-66880}"
DHT_PORT="${DHT_PORT:-66881}"
DEVICE_NAME="${DEVICE_NAME:-$(hostname 2>/dev/null || echo 'CatClaw')}"
RATE_LIMIT="${RATE_LIMIT:-128}"
BOOTSTRAP_NODES="${BOOTSTRAP_NODES:-music.08102516.xyz:6881}"
VERSION="${VERSION:-latest}"
DEPLOY_MODE="${DEPLOY_MODE:-auto}"   # auto | docker | binary | systemd

# ── 解析命令行参数 ──────────────────────────────────────────
for arg in "${@:-}"; do
    case "$arg" in
        docker|binary|systemd|build) DEPLOY_MODE="$arg" ;;
        help|-h|--help)
            echo "用法:  bash deploy.sh [docker|binary|systemd|build]"
            echo ""
            echo "  docker    - Docker 容器部署（拉取预构建镜像）"
            echo "  build     - git clone + docker build 本地编译部署"
            echo "  binary    - 下载预编译二进制直接运行（前台）"
            echo "  systemd   - 二进制 + systemd 服务（后台自启）"
            echo ""
            echo "环境变量:"
            echo "  MUSIC_DIR       音乐目录 (必填)"
            echo "  DEVICE_NAME     设备名称 (默认主机名)"
            echo "  HTTP_PORT       HTTP 端口 (默认 66880)"
            echo "  DHT_PORT        DHT 端口  (默认 66881)"
            echo "  RATE_LIMIT      限速 KB/s  (默认 128)"
            echo ""
            echo "示例:"
            echo "  MUSIC_DIR=/vol1/music bash deploy.sh"
            echo "  MUSIC_DIR=/vol1/music bash deploy.sh systemd"
            echo "  bash deploy.sh    # 交互模式"
            exit 0
            ;;
    esac
done

# ── 智能探测音乐目录 ────────────────────────────────────────
detect_music_dir() {
    for dir in "/vol1/music" "/volume1/music" "/mnt/music" "/mnt/nas/music" "/media/music" "$HOME/music" "$HOME/Music"; do
        if [ -d "$dir" ]; then
            echo "$dir"
            return
        fi
    done
    # 如果都找不到，用默认路径（后续在 Web UI 中修改）
    echo "/vol1/music"
}

if [ -z "$MUSIC_DIR" ]; then
    DETECTED=$(detect_music_dir)
    echo ""
    ask "音乐文件存放目录 [$DETECTED]"
    read -r -p "  > " MUSIC_DIR
    MUSIC_DIR="${MUSIC_DIR:-$DETECTED}"
fi

if [ ! -d "$MUSIC_DIR" ]; then
    warn "目录不存在: $MUSIC_DIR"
    info "服务仍会安装，稍后可在 Web UI 中修改音乐目录"
    info "(目录将在首次扫描前自动创建)"
    mkdir -p "$MUSIC_DIR" 2>/dev/null || true
fi

# ── 自动选择部署模式 ────────────────────────────────────────
if [ "$DEPLOY_MODE" = "auto" ]; then
    if command -v docker &>/dev/null && docker info &>/dev/null 2>&1; then
        DEPLOY_MODE="docker"
        info "检测到 Docker，使用 Docker 模式"
    elif [ "$(id -u)" = "0" ] && [ -d /etc/systemd ]; then
        info "未检测到 Docker，尝试 systemd 模式"
        info "（需要预编译二进制，如失败请安装 Docker 后重试: bash deploy.sh build）"
        DEPLOY_MODE="systemd"
    else
        info "未检测到 Docker，尝试二进制模式"
        info "（需要预编译二进制，如失败请安装 Docker 后重试: bash deploy.sh build）"
        DEPLOY_MODE="binary"
    fi
fi

# ── 显示配置 ────────────────────────────────────────────────
echo ""
info "部署配置确认:"
info "  模式:       $DEPLOY_MODE"
info "  音乐目录:   $MUSIC_DIR"
info "  数据目录:   $DATA_DIR"
info "  HTTP 端口:  $HTTP_PORT"
info "  DHT 端口:   $DHT_PORT"
info "  设备名称:   $DEVICE_NAME"
info "  限速:       ${RATE_LIMIT} KB/s"
echo ""

# ── Docker 模式（拉取预构建镜像）─────────────────────────────
deploy_docker() {
    if ! command -v docker &>/dev/null; then
        warn "未找到 Docker，请先安装"
        info "飞牛OS: 在应用中心安装 Docker"
        info "其他:   curl -fsSL https://get.docker.com | bash"
        exit 1
    fi

    mkdir -p "$DATA_DIR"

    docker rm -f catclaw-server 2>/dev/null || true

    # Try pulling prebuilt image first
    info "拉取预构建镜像..."
    if docker pull ghcr.io/kankejiang/catclaw-server:"$VERSION" 2>/dev/null; then
        log "镜像拉取成功"
    else
        warn "预构建镜像拉取失败，自动切换为本地编译构建..."
        deploy_build
        return
    fi

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

    sleep 2
    IP=$(hostname -I 2>/dev/null | awk '{print $1}' || echo 'localhost')
    log "部署完成！"
    info "Web UI:  http://${IP}:${HTTP_PORT}"
    info "管理:    docker logs -f catclaw-server"
}

# ── 本地构建模式（git clone + docker build）─────────────────
deploy_build() {
    if ! command -v docker &>/dev/null; then
        warn "未找到 Docker"
        exit 1
    fi
    if ! command -v git &>/dev/null; then
        warn "需要 git，请先安装: apt install git / yum install git"
        exit 1
    fi

    BUILD_DIR="$DATA_DIR/build"
    mkdir -p "$DATA_DIR"

    info "克隆项目代码..."
    rm -rf "$BUILD_DIR"
    git clone --depth 1 https://github.com/kankejiang/catclaw-server.git "$BUILD_DIR"

    info "Docker 编译构建 (约 2-3 分钟)..."
    docker build -t catclaw-server:local "$BUILD_DIR"

    rm -rf "$BUILD_DIR"
    log "编译完成"

    docker rm -f catclaw-server 2>/dev/null || true

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
        catclaw-server:local

    sleep 2
    IP=$(hostname -I 2>/dev/null | awk '{print $1}' || echo 'localhost')
    log "部署完成！"
    info "Web UI:  http://${IP}:${HTTP_PORT}"
    info "管理:    docker logs -f catclaw-server"
}

# ── 二进制模式 ──────────────────────────────────────────────
deploy_binary() {
    mkdir -p "$DATA_DIR"

    ARCH=$(uname -m)
    case "$ARCH" in
        x86_64)  ARCH="amd64" ;;
        aarch64|arm64) ARCH="arm64" ;;
        armv7l)  ARCH="armv7" ;;
        *) warn "不支持的 CPU 架构: $ARCH"; exit 1 ;;
    esac

    OS=$(uname -s | tr '[:upper:]' '[:lower:]')
    BINARY="catclaw-server-${OS}-${ARCH}"
    URL="https://github.com/kankejiang/catclaw-server/releases/download/v${VERSION}/${BINARY}"

    info "下载 $BINARY ..."
    if command -v curl &>/dev/null; then
        curl -L --progress-bar -o "$DATA_DIR/catclaw-server" "$URL"
    elif command -v wget &>/dev/null; then
        wget -O "$DATA_DIR/catclaw-server" "$URL"
    else
        warn "需要 curl 或 wget，请先安装"
        exit 1
    fi

    # Check if we got a valid ELF binary or junk (HTML 404)
    if file "$DATA_DIR/catclaw-server" 2>/dev/null | grep -qi 'ELF'; then
        chmod +x "$DATA_DIR/catclaw-server"
        log "二进制已下载到 $DATA_DIR/catclaw-server"
        log "启动 CatClaw Server..."
        echo ""
        exec "$DATA_DIR/catclaw-server" \
            --music-dir="$MUSIC_DIR" \
            --db-path="$DATA_DIR/catclaw.db" \
            --http-port="$HTTP_PORT" \
            --dht-port="$DHT_PORT" \
            --bootstrap="$BOOTSTRAP_NODES" \
            --rate-limit="$RATE_LIMIT" \
            --device-name="$DEVICE_NAME"
    else
        warn "预编译二进制尚未发布 (v${VERSION})"
        info ""
        info "替代方案:"
        info "  1) Docker 部署:  bash deploy.sh docker"
        if command -v go &>/dev/null; then
            info "  2) 源码编译:      git clone ... && go build"
        fi
        info ""
        info "Web UI 访问地址: http://$(hostname -I 2>/dev/null | awk '{print $1}' || echo 'YOUR_IP'):${HTTP_PORT}"
        exit 1
    fi
}

# ── Systemd 模式 ────────────────────────────────────────────
deploy_systemd() {
    if [ "$(id -u)" != "0" ]; then
        warn "systemd 模式需要 root 权限"
        info "请用: sudo bash deploy.sh systemd"
        exit 1
    fi

    mkdir -p "$DATA_DIR"

    # 下载二进制（不上传 stdout，等下载完）
    ARCH=$(uname -m)
    case "$ARCH" in
        x86_64)  ARCH="amd64" ;;
        aarch64|arm64) ARCH="arm64" ;;
        armv7l)  ARCH="armv7" ;;
        *) warn "不支持的 CPU 架构: $ARCH"; exit 1 ;;
    esac

    OS=$(uname -s | tr '[:upper:]' '[:lower:]')
    BINARY="catclaw-server-${OS}-${ARCH}"
    URL="https://github.com/kankejiang/catclaw-server/releases/download/v${VERSION}/${BINARY}"

    info "下载 $BINARY ..."
    if command -v curl &>/dev/null; then
        curl -L --progress-bar -o "$DATA_DIR/catclaw-server" "$URL"
    elif command -v wget &>/dev/null; then
        wget -O "$DATA_DIR/catclaw-server" "$URL"
    else
        warn "需要 curl 或 wget"
        exit 1
    fi
    chmod +x "$DATA_DIR/catclaw-server"

    # Check valid binary
    if ! file "$DATA_DIR/catclaw-server" 2>/dev/null | grep -qi 'ELF'; then
        warn "预编译二进制尚未发布，请使用 Docker 部署: bash deploy.sh docker"
        exit 1
    fi

    # 创建 systemd 服务
    SERVICE_FILE="/etc/systemd/system/catclaw-server.service"
    info "创建 systemd 服务: $SERVICE_FILE"

    cat > "$SERVICE_FILE" << EOF
[Unit]
Description=CatClaw P2P Music Server
After=network.target network-online.target
Wants=network-online.target

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
RestartSec=10
User=root

[Install]
WantedBy=multi-user.target
EOF

    systemctl daemon-reload
    systemctl enable catclaw-server
    systemctl restart catclaw-server

    sleep 2
    IP=$(hostname -I 2>/dev/null | awk '{print $1}' || echo 'localhost')
    echo ""
    log "部署完成！服务已开机自启"
    echo ""
    info "常用命令:"
    info "  systemctl status catclaw-server   查看状态"
    info "  journalctl -u catclaw-server -f   实时日志"
    info "  systemctl restart catclaw-server  重启服务"
    info "  systemctl stop catclaw-server     停止服务"
    echo ""
    info "Web UI:  http://${IP}:${HTTP_PORT}"
}

# ── 执行 ────────────────────────────────────────────────────
case "$DEPLOY_MODE" in
    docker)   deploy_docker ;;
    build)    deploy_build ;;
    binary)   deploy_binary ;;
    systemd)  deploy_systemd ;;
esac
