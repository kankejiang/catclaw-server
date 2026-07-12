#!/bin/bash
# ============================================
#  CatClaw Music Server 增量更新脚本
#  保留配置、只更新代码 → 重建 → 重启
# ============================================
set -e

cd /opt/catclaw-server

echo "========================================"
echo "  CatClaw Music Server 增量更新"
echo "========================================"

# 1. 拉取最新源码
echo "📥 拉取最新代码..."
# 备份配置
cp docker-compose.yml /tmp/docker-compose.yml.bak 2>/dev/null || true
cp .env /tmp/catclaw-env.bak 2>/dev/null || true
if [ -d .git ]; then
    git fetch origin master --depth=1 2>/dev/null || true
    git reset --hard FETCH_HEAD 2>/dev/null || {
        rm -rf .git
        git init .
        git remote add origin https://github.com/kankejiang/catclaw-server.git 2>/dev/null || git remote set-url origin https://github.com/kankejiang/catclaw-server.git
        git fetch origin master --depth=1
        git reset --hard FETCH_HEAD
    }
else
    git init . 2>/dev/null || true
    git remote add origin https://github.com/kankejiang/catclaw-server.git 2>/dev/null || git remote set-url origin https://github.com/kankejiang/catclaw-server.git
    git fetch origin master --depth=1
    git reset --hard FETCH_HEAD
fi
# 恢复配置
cp /tmp/docker-compose.yml.bak docker-compose.yml 2>/dev/null || true
cp /tmp/catclaw-env.bak .env 2>/dev/null || true

# 2. 重建镜像（无缓存，确保新代码生效）
echo "🔨 重建 Docker 镜像（无缓存）..."
docker compose build --no-cache

# 3. 重启容器
echo "🔄 重启服务..."
docker compose up -d

sleep 5
if docker compose ps | grep -q "Up"; then
    echo ""
    echo "========================================"
    echo "  ✅ 更新完成！"
    echo "========================================"
    echo "Web 管理: http://$(hostname -I 2>/dev/null | awk '{print $1}' || echo 'NAS_IP'):37823"
else
    echo "❌ 启动失败，查看日志: docker compose logs"
fi
