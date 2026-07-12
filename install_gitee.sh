#!/bin/bash
# ============================================
#  CatClaw Music Server 一键部署脚本 (Gitee 国内镜像)
# ============================================

set -e

INSTALL_DIR="${INSTALL_DIR:-/opt/catclaw-server}"
REPO_URL="https://gitee.com/kankejiang/catclaw-server.git"

echo "========================================"
echo "  CatClaw Music Server 部署 (Gitee)"
echo "========================================"

if [[ $EUID -ne 0 ]]; then
   echo "❌ 请使用 root 权限运行: sudo bash install_gitee.sh"
   exit 1
fi

if ! command -v docker &> /dev/null; then
    echo "📦 正在安装 Docker..."
    curl -fsSL https://get.docker.com | sh
    systemctl enable docker
    systemctl start docker
    echo "✅ Docker 安装完成"
fi

# ── 先问配置 ──
echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  配置（直接回车使用默认值）"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
read -p "音乐目录 [留空跳过，部署后再设]: " music_dir < /dev/tty
MUSIC_DIR="${music_dir:-/mnt/media/music}"
read -p "监听端口 [37823]: " port < /dev/tty
PORT="${port:-37823}"
read -p "访问令牌（留空自动生成）: " token < /dev/tty
ACCESS_TOKEN="${token:-$(openssl rand -hex 16)}"

mkdir -p "$MUSIC_DIR" 2>/dev/null || echo "⚠️ $MUSIC_DIR 暂不可写，部署后可手动创建并放入音乐文件"

# 最终兜底：确保关键变量不为空（防止 pipe 模式下 read 未读到输入）
if [ -z "$MUSIC_DIR" ]; then
    MUSIC_DIR="/mnt/media/music"
fi

# ── 下载源码 ──
echo "📁 安装目录: $INSTALL_DIR"
mkdir -p "$INSTALL_DIR/data/covers"
cd "$INSTALL_DIR"

echo "📥 下载服务端源码..."
rm -rf .git catclaw-src 2>/dev/null || true
git init catclaw-src
cd catclaw-src
git remote add origin "$REPO_URL"
git config core.sparseCheckout true
echo "/*" > .git/info/sparse-checkout
git pull origin master --depth=1
cd "$INSTALL_DIR"
mv catclaw-src/* catclaw-src/.[!.]* . 2>/dev/null || true
rm -rf catclaw-src .git

# ── 写 .env（仅作参考，docker-compose 不受其影响）──
echo "📝 写入配置..."
cat > .env << ENV
MUSIC=${MUSIC_DIR}
PORT=${PORT}
ACCESS_TOKEN=${ACCESS_TOKEN}
ENV

# ── 写 docker-compose.yml（直接写入实际值，确保启动不受 .env 影响）──
cat > docker-compose.yml << COMPOSE
services:
  catclaw-server:
    build: .
    container_name: catclaw-server
    restart: unless-stopped
    network_mode: host
    environment:
      MusicServer__MusicDirectory: /music
      MusicServer__DbPath: /data/catclaw.db
      MusicServer__CoverOutputDir: /data/covers
      MusicServer__AccessToken: ${ACCESS_TOKEN}
      MusicServer__AdminUser: admin
      MusicServer__AdminPassword: ""
      ASPNETCORE_URLS: "http://0.0.0.0:${PORT}"
    volumes:
      - ${MUSIC_DIR}:/music
      - ./data:/data
COMPOSE

# ── 构建并启动 ──
echo "🔨 构建 Docker 镜像（首次约 2-3 分钟）..."
docker compose up -d --build

echo "⏳ 等待服务启动..."
sleep 8

if docker compose ps | grep -q "Up"; then
    echo ""
    echo "========================================"
    echo "  ✅ CatClaw Music Server 部署成功！"
    echo "========================================"
    echo ""
    echo "Web 管理:  http://$(hostname -I 2>/dev/null | awk '{print $1}' || echo 'NAS_IP'):${PORT}"
    echo "访问令牌:  ${ACCESS_TOKEN}"
    echo ""
    echo "浏览器打开 → 注册管理员 → 开始使用"
    echo ""
    echo "📁 添加音乐文件夹："
    echo "  编辑 docker-compose.yml 中 volumes 的 ${MUSIC_DIR} → docker compose up -d"
    echo ""
    echo "常用命令:"
    echo "  查看日志:  docker compose logs -f"
    echo "  重启服务:  docker compose up -d"
    echo "  停止服务:  docker compose down"
    echo "  重置管理员: docker exec catclaw-server dotnet CatClawMusicServer.dll claw reset"
    echo ""
else
    echo "❌ 启动失败，查看日志: docker compose logs"
fi
