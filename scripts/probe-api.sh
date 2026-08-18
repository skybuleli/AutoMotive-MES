#!/usr/bin/env bash
# =====================================================================
#  AutoMES 全接口探测脚本（联调冒烟测试）
#  构建并启动 REST API → 多角色 token 探测全部前端可达的 GET/POST/PUT/PATCH
#  端点 → 报告状态码（404 缺路由 / 500 服务器错误视为失败）→ 清理进程。
#
#  用法：
#    ./scripts/probe-api.sh
#
#  前置：
#    PostgreSQL 已运行：docker compose -f docker/compose.dev.yaml up -d
#
#  退出码：0 = 全部端点路由可达；1 = 存在 404/500 或启动失败。
# =====================================================================
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API_DIR="$ROOT/src/MesAdmin.Api"
API_URL="http://localhost:5040"
LOG="/tmp/automes_probe_api.log"
PIDFILE="/tmp/automes_probe_api.pid"
# 路由参数占位符：非法 Ulid → 命中路由后由端点返回 400（而非路由级 404），可区分「路由缺失」。
D="01H000000000000000000000000"

RED=$'\033[0;31m'; GREEN=$'\033[0;32m'; BLUE=$'\033[0;34m'; NC=$'\033[0m'
TOTAL=0; FAIL=0

info() { printf "${BLUE}[probe]${NC} %s\n" "$*"; }

# 探测单个端点：404/500 视为失败，其余（200/400/401/403/409/422）视为路由存在。
probe() { # <METHOD> <PATH> <TOKEN>
  local m=$1 p=$2 t=$3 code
  if [ "$m" = "GET" ]; then
    code=$(curl -s -o /dev/null -w "%{http_code}" "$API_URL$p" -H "Authorization: Bearer $t" || true)
  else
    code=$(curl -s -o /dev/null -w "%{http_code}" -X "$m" "$API_URL$p" \
      -H "Authorization: Bearer $t" -H "Content-Type: application/json" -d '{}' || true)
  fi
  TOTAL=$((TOTAL+1))
  if [ "$code" = "404" ] || [ "$code" = "500" ]; then
    printf "  ${RED}%-6s %-52s %s${NC}\n" "$m" "$p" "$code"
    FAIL=$((FAIL+1))
  else
    printf "  ${GREEN}%-6s${NC} %-52s %s\n" "$m" "$p" "$code"
  fi
}

# ── 0. 前置检查 ──
if ! nc -z -w 2 localhost 5432 2>/dev/null; then
  echo "PostgreSQL (5432) 未运行，请先启动：docker compose -f docker/compose.dev.yaml up -d"
  exit 1
fi

# ── 1. 构建 ──
info "构建 MesAdmin.Api ..."
dotnet build "$API_DIR/MesAdmin.Api.csproj" -nologo -clp:NoSummary -v:q >/dev/null

API_DLL="$(find "$API_DIR/bin/Debug" -maxdepth 2 -name 'MesAdmin.Api.dll' 2>/dev/null | head -1)"
if [ -z "$API_DLL" ]; then
  echo "未找到 API 编译产物（$API_DIR/bin/Debug/**/MesAdmin.Api.dll）"
  exit 1
fi

# ── 2. 启动 ──
info "启动 API ($API_URL) ..."
lsof -ti tcp:5040 2>/dev/null | xargs -r kill 2>/dev/null || true
sleep 1
( cd "$API_DIR" && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="$API_URL" \
  dotnet "$API_DLL" > "$LOG" 2>&1 & echo $! > "$PIDFILE" )

cleanup() {
  [ -f "$PIDFILE" ] && kill "$(cat "$PIDFILE")" 2>/dev/null || true
  lsof -ti tcp:5040 2>/dev/null | xargs -r kill 2>/dev/null || true
}
trap cleanup EXIT

for _ in $(seq 1 60); do nc -z -w 1 localhost 5040 2>/dev/null && break; sleep 1; done
if ! nc -z -w 1 localhost 5040 2>/dev/null; then
  echo "API 启动失败，日志尾部："
  tail -40 "$LOG"
  exit 1
fi
info "API 已就绪"

# ── 3. 登录获取各角色 token ──
tok() { curl -s -X POST "$API_URL/api/auth/login" -H "Content-Type: application/json" \
  -d "{\"Username\":\"$1\",\"Password\":\"x\"}" | sed -E 's/.*"token":"([^"]+)".*/\1/'; }
M=$(tok manager); Q=$(tok qe); W=$(tok warehouse); L=$(tok leader)

# ── 4. GET 端点（404/500 = 失败）──
info "探测 GET 端点 ..."
probe GET "/api/v1/andon" "$M"
probe GET "/api/v1/andon/stats" "$M"
probe GET "/api/v1/dashboard/summary" "$M"
probe GET "/api/v1/inventory/monitoring" "$M"
probe GET "/api/v1/jit-kanban/pending" "$M"
probe GET "/api/v1/jit-kanban/history?page=1&size=10" "$M"
probe GET "/api/v1/maintenance/plans" "$M"
probe GET "/api/v1/maintenance/orders" "$M"
probe GET "/api/v1/maintenance/spare-parts" "$M"
probe GET "/api/v1/maintenance/purchase-requests" "$M"
probe GET "/api/v1/materials/batches?page=1&size=15" "$M"
probe GET "/api/v1/orders" "$M"
probe GET "/api/v1/quality/8d" "$Q"
probe GET "/api/v1/quality/ncr" "$Q"
probe GET "/api/v1/quality/records?stage=Iq" "$Q"
probe GET "/api/v1/quality/spc/alerts" "$Q"
probe GET "/api/v1/routing" "$M"
probe GET "/api/v1/routing/ESP-9.0/default" "$M"
probe GET "/api/v1/scheduling/plans" "$M"
probe GET "/api/v1/scheduling/capacity?date=2026-08-17" "$M"
probe GET "/api/v1/scheduling/gantt-data?from=2026-08-17&to=2026-08-18" "$M"
probe GET "/api/v1/suppliers/suppliers" "$M"
probe GET "/api/v1/suppliers/suppliers/critical-settings" "$M"

# ── 5. POST/PUT/PATCH 端点（{} body，仅验证路由存在）──
info "探测 POST/PUT/PATCH 端点 ..."
probe POST "/api/v1/andon/$D/acknowledge" "$M"
probe POST "/api/v1/andon/$D/resolve" "$M"
probe POST "/api/v1/andon/$D/close" "$M"
probe POST "/api/v1/jit-kanban/$D/deliver" "$M"
probe POST "/api/v1/jit-kanban/$D/cancel" "$M"
probe POST "/api/v1/jit-kanban/create" "$M"
probe POST "/api/v1/maintenance/spare-parts" "$M"
probe PUT  "/api/v1/maintenance/spare-parts/$D/stock" "$M"
probe POST "/api/v1/maintenance/spare-parts/$D/restock" "$M"
probe POST "/api/v1/maintenance/spare-parts/$D/check-stock" "$M"
probe POST "/api/v1/maintenance/orders/$D/spare-parts" "$M"
probe POST "/api/v1/maintenance/purchase-requests" "$M"
probe POST "/api/v1/maintenance/purchase-requests/$D/approve" "$M"
probe POST "/api/v1/maintenance/purchase-requests/$D/cancel" "$M"
probe POST "/api/v1/materials/batches" "$W"
probe POST "/api/v1/materials/batches/$D/qualify" "$Q"
probe POST "/api/v1/materials/batches/$D/reject" "$Q"
probe POST "/api/v1/materials/bindings" "$M"
probe POST "/api/v1/orders" "$M"
probe POST "/api/v1/orders/$D/start" "$M"
probe POST "/api/v1/orders/$D/complete" "$M"
probe POST "/api/v1/orders/$D/close" "$M"
probe POST "/api/v1/orders/$D/cancel" "$M"
probe POST "/api/v1/orders/$D/kit-check" "$M"
probe PATCH "/api/v1/orders/$D/status" "$M"
probe POST "/api/v1/orders/$D/operations/1/report" "$M"
probe POST "/api/v1/quality/8d" "$Q"
probe POST "/api/v1/quality/8d/$D/close" "$Q"
probe POST "/api/v1/quality/ncr" "$Q"
probe POST "/api/v1/quality/ncr/$D/review" "$Q"
probe POST "/api/v1/quality/ncr/$D/disposition" "$Q"
probe POST "/api/v1/quality/ncr/$D/close" "$Q"
probe POST "/api/v1/quality/spc/samples" "$Q"
probe POST "/api/v1/quality/spc/alerts/$D/ack" "$Q"
probe POST "/api/v1/routing" "$M"
probe POST "/api/v1/routing/$D/submit" "$M"
probe POST "/api/v1/routing/$D/approve" "$M"
probe POST "/api/v1/routing/$D/release" "$M"
probe POST "/api/v1/routing/verify" "$M"
probe POST "/api/v1/scheduling/plans" "$M"
probe POST "/api/v1/scheduling/plans/$D/start" "$M"
probe POST "/api/v1/scheduling/plans/$D/complete" "$M"
probe POST "/api/v1/scheduling/plans/$D/cancel" "$M"
probe POST "/api/v1/scheduling/rush-order" "$M"
probe POST "/api/v1/suppliers/suppliers" "$M"
probe PUT  "/api/v1/suppliers/suppliers/$D" "$M"
probe POST "/api/v1/suppliers/suppliers/$D/update-tier" "$M"
probe POST "/api/v1/suppliers/suppliers/$D/score" "$M"
probe POST "/api/v1/suppliers/suppliers/$D/ppap" "$M"
probe POST "/api/v1/suppliers/suppliers/$D/ppap/$D/submit" "$M"
probe POST "/api/v1/suppliers/suppliers/$D/ppap/$D/approve" "$M"
probe POST "/api/v1/suppliers/suppliers/$D/ppap/$D/reject" "$M"
probe POST "/api/v1/suppliers/suppliers/critical-settings" "$M"
probe POST "/api/v1/traceability/bind" "$M"

# ── 6. 汇总 ──
echo
if [ "$FAIL" -eq 0 ]; then
  printf "${GREEN}探测完成：%s 端点，0 失败${NC}\n" "$TOTAL"
  exit 0
else
  printf "${RED}探测完成：%s 端点，%s 失败${NC}\n" "$TOTAL" "$FAIL"
  exit 1
fi
