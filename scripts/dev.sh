#!/usr/bin/env bash
# =====================================================================
#  AutoMES 一键启动开发环境：PostgreSQL → REST API → Web 管理后台
#
#  用法：
#    ./scripts/dev.sh
#
#  行为：
#    - PostgreSQL 未运行时自动拉起 docker/compose.dev.yaml
#    - 后台启动 REST API   (http://localhost:5040)
#    - 后台启动 Web 管理后台 (http://localhost:5138)
#    - 等待两者就绪后打印访问地址
#    - Ctrl+C 停止全部服务（含兜底按端口清理残留进程）
#
#  日志：/tmp/automes-dev/api.log 与 /tmp/automes-dev/web.log
# =====================================================================
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE="docker/compose.dev.yaml"
PG_PORT=5432
API_PORT=5040
WEB_PORT=5138
API_URL="http://localhost:$API_PORT"
WEB_URL="http://localhost:$WEB_PORT"
LOGDIR="${TMPDIR:-/tmp}/automes-dev"
mkdir -p "$LOGDIR"

BLUE=$'\033[0;34m'; GREEN=$'\033[0;32m'; YELLOW=$'\033[0;33m'; RED=$'\033[0;31m'; NC=$'\033[0m'
info() { printf "${BLUE}[dev]${NC} %s\n" "$*"; }
ok()   { printf "${GREEN}[ok]${NC} %s\n" "$*"; }
warn() { printf "${YELLOW}[warn]${NC} %s\n" "$*"; }

API_PID=""; WEB_PID=""

# 释放端口上残留的旧进程（本项目的专属端口，几乎必为上次未清理的 AutoMES 进程）
free_port() { # <port>
  local pids
  pids=$(lsof -ti tcp:"$1" 2>/dev/null || true)
  if [ -n "$pids" ]; then
    warn "端口 $1 被占用（残留进程），清理：$pids"
    echo "$pids" | xargs kill 2>/dev/null || true
    sleep 1
  fi
}

cleanup() {
  info "停止服务 ..."
  [ -n "$API_PID" ] && kill "$API_PID" 2>/dev/null || true
  [ -n "$WEB_PID" ] && kill "$WEB_PID" 2>/dev/null || true
  # 兜底：dotnet run 的子进程可能未被 wrapper kill，按端口清理真正占用者
  lsof -ti tcp:$API_PORT 2>/dev/null | xargs -r kill 2>/dev/null || true
  lsof -ti tcp:$WEB_PORT 2>/dev/null | xargs -r kill 2>/dev/null || true
}
trap cleanup EXIT INT TERM

# ── 1. PostgreSQL ──
if nc -z -w 2 localhost $PG_PORT 2>/dev/null; then
  ok "PostgreSQL 已在运行 (:$PG_PORT)"
else
  info "PostgreSQL 未运行，启动 docker compose ..."
  docker compose -f "$ROOT/$COMPOSE" up -d
  for _ in $(seq 1 30); do nc -z -w 1 localhost $PG_PORT 2>/dev/null && break; sleep 1; done
  if nc -z -w 1 localhost $PG_PORT 2>/dev/null; then
    ok "PostgreSQL 就绪 (:$PG_PORT)"
  else
    echo "${RED}PostgreSQL 启动失败，请检查 docker compose 日志。${NC}"
    exit 1
  fi
fi

# ── 2. 释放端口并启动 API ──
free_port $API_PORT
free_port $WEB_PORT

info "启动 API ($API_URL) ..."
( cd "$ROOT/src/MesAdmin.Api" \
  && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="$API_URL" \
     dotnet run --project MesAdmin.Api.csproj > "$LOGDIR/api.log" 2>&1 ) &
API_PID=$!

info "启动 Web ($WEB_URL) ..."
( cd "$ROOT/src/MesAdmin.Web" \
  && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="$WEB_URL" \
     dotnet run --project MesAdmin.Web.csproj > "$LOGDIR/web.log" 2>&1 ) &
WEB_PID=$!

# ── 3. 等待就绪（首次启动含 restore/build，最长约 90s）──
info "等待服务就绪 ..."
api_ready=0; web_ready=0
for _ in $(seq 1 90); do
  [ "$api_ready" -eq 0 ] && nc -z -w 1 localhost $API_PORT 2>/dev/null && api_ready=1
  [ "$web_ready" -eq 0 ] && nc -z -w 1 localhost $WEB_PORT 2>/dev/null && web_ready=1
  [ "$api_ready" -eq 1 ] && [ "$web_ready" -eq 1 ] && break
  sleep 1
done

if [ "$api_ready" -eq 0 ]; then
  echo "${RED}API 启动失败，日志尾部：${NC}"
  tail -30 "$LOGDIR/api.log"
  exit 1
fi
if [ "$web_ready" -eq 0 ]; then
  echo "${RED}Web 启动失败，日志尾部：${NC}"
  tail -30 "$LOGDIR/web.log"
  exit 1
fi

# ── 4. 就绪提示 ──
echo
ok "全部就绪："
printf "  API   ${GREEN}%s${NC}   (日志 %s)\n" "$API_URL" "$LOGDIR/api.log"
printf "  Web   ${GREEN}%s${NC}   (日志 %s)\n" "$WEB_URL" "$LOGDIR/web.log"
printf "  登录  账号 manager / leader / qe / ee / warehouse / sqe（密码任意）\n"
echo
info "按 Ctrl+C 停止全部服务"

wait "$API_PID" "$WEB_PID"
