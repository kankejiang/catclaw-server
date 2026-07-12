#!/bin/bash
# ============================================
#  CatClaw Music Server 一键部署脚本 (Gitee 国内镜像)
#  
# ============================================

set -e

INSTALL_DIR="${INSTALL_DIR:-/opt/catclaw-server}"
PORT="${PORT:-37823}"
REPO_URL="https://gitee.com/kankejiang/catclaw-server.git"

echo "========================================"
echo "  CatClaw Music Server 部署 (Gitee)"
echo "========================================"

# 检查 root 权限
if [[ $EUID -ne 0 ]]; then
   echo "❌ 请使用 root 权限运行: sudo bash install.sh"
   exit 1
fi

# 检查 / 安装 Docker
if ! command -v docker &> /dev/null; then
    echo "📦 正在安装 Docker..."
    curl -fsSL https://get.docker.com | sh
    systemctl enable docker
    systemctl start docker
    echo "✅ Docker 安装完成"
fi

# 创建安装目录
echo "📁 创建安装目录: $INSTALL_DIR"
mkdir -p "$INSTALL_DIR/data/covers"
cd "$INSTALL_DIR"

# 下载服务端源码（sparse-checkout 只取服务端）
echo "📥 下载服务端源码..."
rm -rf .git catclaw-src 2>/dev/null || true

git init catclaw-src
cd catclaw-src
git remote add origin "$REPO_URL"
git config core.sparseCheckout true
echo "/*" > .git/info/sparse-checkout
git pull origin main --depth=1
cd "$INSTALL_DIR"
mv catclaw-src/* catclaw-src/.[!.]* . 2>/dev/null || true
rm -rf catclaw-src .git

# 创建 docker-compose.yml
echo "📝 创建 docker-compose.yml..."
cat > docker-compose.yml << 'COMPOSE'
services:
  catclaw-server:
    build: .
    container_name: catclaw-server
    restart: unless-stopped
    network_mode: host
    environment:
      - MusicServer__MusicDirectory=/music
      - MusicServer__DbPath=/data/catclaw.db
      - MusicServer__CoverOutputDir=/data/covers
      - MusicServer__AccessToken=${ACCESS_TOKEN:-}
      - MusicServer__AdminUser=admin
      - MusicServer__AdminPassword=
      - ASPNETCORE_URLS=http://0.0.0.0:${PORT:-37823}
    volumes:
      - ${MUSIC_DIR:-/mnt/media/music}:/music
      - ./data:/data
COMPOSE

# 问用户配置
echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  配置信息（直接回车使用默认值）"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
read -p "音乐目录路径 [/mnt/media/music]: " music_dir
MUSIC_DIR="${music_dir:-/mnt/media/music}"
read -p "监听端口 [37823]: " port
PORT="${port:-37823}"
read -p "访问令牌（留空自动生成）: " token
ACCESS_TOKEN="${token:-$(openssl rand -hex 16)}"

mkdir -p "$MUSIC_DIR" 2>/dev/null || echo "⚠️ 音乐目录 $MUSIC_DIR 不存在，请确保有音乐文件"

# 替换环境变量
sed -i "s|\${MUSIC_DIR:-/mnt/media/music}|${MUSIC_DIR}|g" docker-compose.yml
sed -i "s|\${PORT:-37823}|${PORT}|g" docker-compose.yml
sed -i "s|\${ACCESS_TOKEN:-}|${ACCESS_TOKEN}|g" docker-compose.yml

# 构建并启动
echo ""
echo "🔨 正在构建 Docker 镜像（首次约 2-3 分钟）..."
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
    echo "首次使用会自动进入注册页创建管理员"
    echo ""
    echo "常用命令:"
    echo "  查看日志:  docker compose logs -f"
    echo "  重启服务:  docker compose restart"
    echo "  停止服务:  docker compose down"
    echo "  重置管理员: docker exec catclaw-server dotnet CatClawMusicServer.dll claw reset"
    echo ""
else
    echo "❌ 启动失败，查看日志: docker compose logs"
fi
