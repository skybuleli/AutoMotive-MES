#!/usr/bin/env bash
# =====================================================================
#  AutoMES Uncloud 一键部署：构建 → 健康门禁滚动发布 → 冒烟测试
#
#  用法：
#    ./scripts/deploy-uncloud.sh [context]        # 默认 context: automes-local
#    ./scripts/deploy-uncloud.sh automes-local mes-web   # 只部署指定服务
#
#  依赖：
#    - uc （Uncloud CLI，默认 /opt/homebrew/bin/uc，可用 UC= 环境变量覆盖）
#    - ./.env.uncloud.local （POSTGRES_PASSWORD / JWT_SECRET 等）
#
#  镜像仓库模式（跳过本机构建，直接拉取 CI 推送的 GHCR 镜像）：
#    IMAGE_TAG=<git-sha 或 latest> ./scripts/deploy-uncloud.sh --registry
#    前提：本机已 docker login ghcr.io（uc deploy 会把凭证传给目标机器）
#
#  其他行为：
#    - 每次部署前自动 pg_dump 备份（走 pg-backup 容器，存 backups/，--no-backup 跳过）
#    - 部署后清理 VM 上的悬空镜像（--no-prune 跳过；OrbStack VM 自动识别）
# =====================================================================
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

REGISTRY_MODE=0; NO_BACKUP=0; NO_PRUNE=0
for arg in "$@"; do
  case "$arg" in
    --registry)  REGISTRY_MODE=1 ;;
    --no-backup) NO_BACKUP=1 ;;
    --no-prune)  NO_PRUNE=1 ;;
  esac
done
# 过滤掉 flag，仅保留 context 与服务名
ARGS=()
for arg in "$@"; do
  case "$arg" in --registry|--no-backup|--no-prune) ;; *) ARGS+=("$arg") ;; esac
done
[ ${#ARGS[@]} -gt 0 ] && CONTEXT="${ARGS[0]}" || CONTEXT="automes-local"
[ ${#ARGS[@]} -gt 1 ] && set -- "${ARGS[@]:1}" || set --
COMPOSE_FILE="docker/compose.uncloud.yaml"
UC="${UC:-/opt/homebrew/bin/uc}"
WEB_PORT=15138

BLUE=$'\033[0;34m'; GREEN=$'\033[0;32m'; YELLOW=$'\033[0;33m'; RED=$'\033[0;31m'; NC=$'\033[0m'
info() { printf "${BLUE}[deploy]${NC} %s\n" "$*"; }
ok()   { printf "${GREEN}[ok]${NC} %s\n" "$*"; }
fail() { printf "${RED}[fail]${NC} %s\n" "$*"; exit 1; }

command -v "$UC" >/dev/null || fail "找不到 uc：$UC（可用 UC=/path/to/uc 覆盖）"
[ -f "$ROOT/.env.uncloud.local" ] || fail "缺少 $ROOT/.env.uncloud.local"

cd "$ROOT"

info "加载 .env.uncloud.local"
set -a
. ./.env.uncloud.local
set +a

# ── 部署前数据库备份（迁移不可逆，先留后悔药）──
BACKUP_DIR="$ROOT/backups"
mkdir -p "$BACKUP_DIR"
BACKUP_FILE="$BACKUP_DIR/automes-pre-deploy-$(date +%Y%m%d-%H%M%S).dump"
if [ "$NO_BACKUP" = "1" ]; then
  warn "已跳过部署前备份（--no-backup）"
elif ! "$UC" service inspect -c "$CONTEXT" pg-backup >/dev/null 2>&1; then
  warn "pg-backup 服务不存在（首次部署），跳过备份"
else
  info "备份数据库 → backups/$(basename "$BACKUP_FILE")"
  "$UC" service exec -c "$CONTEXT" pg-backup -- \
    pg_dump -Fc -h postgres -U mes automes > "$BACKUP_FILE" \
    || { rm -f "$BACKUP_FILE"; fail "备份失败，中止部署（确认无误可用 --no-backup 跳过）"; }
  [ -s "$BACKUP_FILE" ] || { rm -f "$BACKUP_FILE"; fail "备份文件为空，中止部署"; }
  ok "备份完成：$(du -h "$BACKUP_FILE" | cut -f1)（保留最近 10 份）"
  ls -t "$BACKUP_DIR"/automes-pre-deploy-*.dump 2>/dev/null | tail -n +11 | xargs rm -f 2>/dev/null
fi

# uc deploy 在本机用 BuildKit 构建镜像（层缓存 + NuGet 缓存挂载都在本机复用），
# 构建完成后推送镜像并在远端执行健康门禁滚动发布；-y 跳过交互确认。
DEPLOY_FLAGS=(-y)
if [ "$REGISTRY_MODE" = "1" ]; then
  IMAGE_TAG="${IMAGE_TAG:-latest}"
  export IMAGE_TAG   # uc deploy 子进程读取 compose 插值变量，必须导出
  docker manifest inspect "ghcr.io/skybuleli/automotive-mes/mes-api:${IMAGE_TAG}" >/dev/null 2>&1 \
    || fail "GHCR 上找不到 mes-api:${IMAGE_TAG}（检查 IMAGE_TAG 或先 docker login ghcr.io）"
  info "镜像仓库模式：IMAGE_TAG=${IMAGE_TAG}，跳过本机构建"
  DEPLOY_FLAGS+=(--no-build)
fi

info "uc deploy → context: $CONTEXT${1:+ (services: $*)}"
"$UC" deploy -f "$COMPOSE_FILE" -c "$CONTEXT" "${DEPLOY_FLAGS[@]}" "$@"

info "等待 Uncloud Caddy 端口就绪 ..."
SMOKE_OK=0
for i in $(seq 1 30); do
  if curl -fsS -o /dev/null "http://localhost:${WEB_PORT}/"; then
    ok "Web 冒烟测试通过: http://localhost:${WEB_PORT}/"
    SMOKE_OK=1
    break
  fi
  sleep 2
done
[ "$SMOKE_OK" = "1" ] || fail "Web 首页 90 秒内未就绪，请查看: $UC logs -c $CONTEXT mes-web"

# ── 部署后清理 VM 悬空镜像（历次部署累积的旧层）──
if [ "$NO_PRUNE" = "1" ]; then
  warn "已跳过 VM 镜像清理（--no-prune）"
else
  MACHINE="$("$UC" machine ls -c "$CONTEXT" -o json 2>/dev/null | python3 -c 'import json,sys; m=json.load(sys.stdin)[0]; print(m["Name"])' 2>/dev/null || true)"
  SSH_TRY=()
  if [ -n "${DEPLOY_SSH:-}" ]; then
    SSH_TRY+=("$DEPLOY_SSH")
  elif [ -n "$MACHINE" ]; then
    SSH_TRY+=("root@${MACHINE}@orb" "root@${MACHINE}")
  fi
  PRUNED=0
  for t in ${SSH_TRY[@]+"${SSH_TRY[@]}"}; do
    if /usr/bin/ssh -o ConnectTimeout=5 -o BatchMode=yes "$t" "docker image prune -f" >/dev/null 2>&1; then
      ok "已清理 VM（${t}）悬空镜像"; PRUNED=1; break
    fi
  done
  if [ "$PRUNED" = "0" ]; then
    warn "VM 镜像清理未执行（可用 DEPLOY_SSH=user@host 指定目标）"
  fi
fi
