#!/usr/bin/env bash
# =====================================================================
#  AutoMES Uncloud 一键部署：备份 → 构建/拉取 → 健康门禁滚动发布 → 验证 → 清理
#
#  用法：
#    ./scripts/deploy-uncloud.sh [options] [context] [service...]
#
#  选项：
#    --registry        镜像仓库模式：跳过本机构建，拉取 GHCR 镜像
#                      （IMAGE_TAG=<git短sha|latest>，默认 latest；
#                       日常部署建议传 sha——latest 标签不变时 Uncloud 不会重新拉取）
#    --rollback [N]    回滚到倒数第 N 个历史版本（默认 1=上一个；
#                       候选 = git 一级父历史 ∩ GHCR sha 标签）
#    --verify-backup   部署前把最近一次备份恢复到临时库验证完整性
#    --no-backup       跳过部署前备份
#    --no-prune        跳过部署后 VM 悬空镜像清理
#
#  依赖：
#    - uc （Uncloud CLI，默认 /opt/homebrew/bin/uc，可用 UC= 环境变量覆盖）
#    - ./.env.uncloud.local （POSTGRES_PASSWORD / JWT_SECRET 等）
#    - registry 模式首次使用需本机 docker login ghcr.io
#      （uc deploy 会自动把凭证传给目标机器）
# =====================================================================
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="docker/compose.uncloud.yaml"
UC="${UC:-/opt/homebrew/bin/uc}"
WEB_PORT=15138
IMAGE_REPO="ghcr.io/skybuleli/automotive-mes"
KEEP_BACKUPS=10

BLUE=$'\033[0;34m'; GREEN=$'\033[0;32m'; YELLOW=$'\033[0;33m'; RED=$'\033[0;31m'; NC=$'\033[0m'
info() { printf "${BLUE}[deploy]${NC} %s\n" "$*"; }
ok()   { printf "${GREEN}[ok]${NC}   %s\n" "$*"; }
warn() { printf "${YELLOW}[warn]${NC} %s\n" "$*"; }
fail() { printf "${RED}[fail]${NC} %s\n" "$*" >&2; exit 1; }

command -v "$UC" >/dev/null || fail "找不到 uc：$UC（可用 UC=/path/to/uc 覆盖）"
[ -f "$ROOT/.env.uncloud.local" ] || fail "缺少 $ROOT/.env.uncloud.local"

# ── 参数解析 ──
REGISTRY_MODE=0; NO_BACKUP=0; NO_PRUNE=0; VERIFY_BACKUP=0; ROLLBACK=""
ARGS=()
while [ $# -gt 0 ]; do
  case "$1" in
    --registry)      REGISTRY_MODE=1 ;;
    --no-backup)     NO_BACKUP=1 ;;
    --no-prune)      NO_PRUNE=1 ;;
    --verify-backup) VERIFY_BACKUP=1 ;;
    --rollback)
      if [ -n "${2:-}" ] && [ "$2" -eq "$2" ] 2>/dev/null; then ROLLBACK="$2"; shift; else ROLLBACK=1; fi
      ;;
    *) ARGS+=("$1") ;;
  esac
  shift
done
[ ${#ARGS[@]} -gt 0 ] && CONTEXT="${ARGS[0]}" || CONTEXT="automes-local"
if [ ${#ARGS[@]} -gt 1 ]; then set -- "${ARGS[@]:1}"; else set --; fi

info "加载 .env.uncloud.local"
set -a
. ./.env.uncloud.local
set +a

# ── 回滚目标解析：git 一级父历史 ∩ GHCR sha 标签，按提交时间从新到旧 ──
if [ -n "$ROLLBACK" ]; then
  REGISTRY_MODE=1
  info "解析回滚候选（git 历史 × GHCR 标签）..."
  TOKEN=$(curl -fsS "https://ghcr.io/token?scope=repository:skybuleli/automotive-mes/mes-api:pull" \
    | python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])')
  TAGS=$(curl -fsS -H "Authorization: Bearer $TOKEN" \
    "https://ghcr.io/v2/skybuleli/automotive-mes/mes-api/tags/list" \
    | python3 -c 'import json,sys; print("\n".join(t for t in json.load(sys.stdin)["tags"] if len(t) == 7))')
  [ -n "$TAGS" ] || fail "GHCR 上没有任何 sha 标签，无从回滚"
  TARGET=$(git -C "$ROOT" log --first-parent --format=%h main \
    | while read -r c; do printf '%s\n' "$TAGS" | grep -qx "$c" && echo "$c"; done \
    | sed -n "$((ROLLBACK + 1))p")
  [ -n "$TARGET" ] || fail "回滚目标不存在（候选见上；N 太大？）"
  docker manifest inspect "${IMAGE_REPO}/mes-api:${TARGET}" >/dev/null 2>&1 \
    || fail "GHCR 上找不到 mes-api:${TARGET}"
  IMAGE_TAG="$TARGET"
  export IMAGE_TAG
  ok "回滚目标：${TARGET}"
fi

# ── 部署前备份（写入 VM pg-backup-data 卷，并同步一份到本机 backups/）──
TS=$(date +%Y%m%d-%H%M%S)
VM_DUMP="/backups/automes-pre-deploy-${TS}.dump"
LOCAL_BACKUPS="$ROOT/backups"
mkdir -p "$LOCAL_BACKUPS"

if [ "$NO_BACKUP" = "1" ]; then
  warn "已跳过部署前备份（--no-backup）"
elif ! "$UC" service inspect -c "$CONTEXT" pg-backup >/dev/null 2>&1; then
  warn "pg-backup 服务不存在（首次部署），跳过备份"
else
  info "备份数据库 → VM:/backups + 本机 backups/"
  "$UC" service exec -c "$CONTEXT" pg-backup -- \
    pg_dump -Fc -h postgres -U mes automes -f "$VM_DUMP" \
    || { fail "备份失败，中止部署（确认无误可用 --no-backup 跳过）"; }
  "$UC" service exec -c "$CONTEXT" pg-backup -- sh -c "test -s $VM_DUMP" \
    || { fail "备份文件为空，中止部署"; }
  "$UC" service exec -c "$CONTEXT" pg-backup -- cat "$VM_DUMP" \
    > "$LOCAL_BACKUPS/automes-pre-deploy-${TS}.dump"
  ok "备份完成：$(du -h "$LOCAL_BACKUPS/automes-pre-deploy-${TS}.dump" | cut -f1)（卷与本机各一份，保留 ${KEEP_BACKUPS} 份）"
  # 卷上与本机各保留最近 KEEP_BACKUPS 份
  "$UC" service exec -c "$CONTEXT" pg-backup -- sh -c \
    "ls -t /backups/automes-pre-deploy-*.dump 2>/dev/null | tail -n +$((KEEP_BACKUPS + 1)) | xargs -r rm -f" \
    || true
  ls -t "$LOCAL_BACKUPS"/automes-pre-deploy-*.dump 2>/dev/null | tail -n +$((KEEP_BACKUPS + 1)) | xargs rm -f 2>/dev/null || true
fi

# ── 备份恢复演练：把最近一次备份恢复到临时库并校验表数量 ──
if [ "$VERIFY_BACKUP" = "1" ]; then
  info "恢复演练：恢复最近备份到临时库 automes_verify ..."
  LATEST_DUMP=$("$UC" service exec -c "$CONTEXT" pg-backup -- \
    sh -c "ls -t /backups/automes-pre-deploy-*.dump 2>/dev/null | head -1" | tr -d '[:space:]')
  [ -n "$LATEST_DUMP" ] || fail "卷上没有可验证的备份"
  SRC_TABLES=$("$UC" service exec -c "$CONTEXT" pg-backup -- \
    psql -h postgres -U mes -d automes -tAc "select count(*) from pg_tables where schemaname = 'public'" | tr -d '[:space:]')
  "$UC" service exec -c "$CONTEXT" pg-backup -- \
    dropdb --if-exists -h postgres -U mes automes_verify >/dev/null
  "$UC" service exec -c "$CONTEXT" pg-backup -- \
    createdb -h postgres -U mes automes_verify >/dev/null
  if "$UC" service exec -c "$CONTEXT" pg-backup -- \
      pg_restore -h postgres -U mes -d automes_verify --no-owner "$LATEST_DUMP" >/dev/null 2>&1; then
    DST_TABLES=$("$UC" service exec -c "$CONTEXT" pg-backup -- \
      psql -h postgres -U mes -d automes_verify -tAc "select count(*) from pg_tables where schemaname = 'public'" | tr -d '[:space:]')
    if [ "$SRC_TABLES" = "$DST_TABLES" ] && [ "$DST_TABLES" != "0" ]; then
      ok "恢复演练通过：公开表数量一致（${DST_TABLES} 张）"
    else
      fail "恢复演练失败：表数量不一致（源 ${SRC_TABLES} / 恢复 ${DST_TABLES}）"
    fi
  else
    fail "pg_restore 执行失败，备份疑似损坏"
  fi
  "$UC" service exec -c "$CONTEXT" pg-backup -- \
    dropdb --if-exists -h postgres -U mes automes_verify >/dev/null
  ok "临时库已清理"
fi

# ── 部署 ──
DEPLOY_FLAGS=(-y)
if [ "$REGISTRY_MODE" = "1" ] && [ -z "$ROLLBACK" ]; then
  IMAGE_TAG="${IMAGE_TAG:-latest}"
  export IMAGE_TAG   # uc deploy 子进程读取 compose 插值变量，必须导出
  docker manifest inspect "${IMAGE_REPO}/mes-api:${IMAGE_TAG}" >/dev/null 2>&1 \
    || fail "GHCR 上找不到 mes-api:${IMAGE_TAG}（检查 IMAGE_TAG 或先 docker login ghcr.io）"
  info "镜像仓库模式：IMAGE_TAG=${IMAGE_TAG}，跳过本机构建"
  DEPLOY_FLAGS+=(--no-build)
fi

info "uc deploy → context: $CONTEXT${1:+ (services: $*)}"
"$UC" deploy -f "$COMPOSE_FILE" -c "$CONTEXT" "${DEPLOY_FLAGS[@]}" "$@"

# ── 冒烟测试 ──
info "等待 Uncloud Caddy 端口就绪 ..."
SMOKE_OK=0
for _ in $(seq 1 30); do
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
