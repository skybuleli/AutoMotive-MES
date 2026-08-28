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
#    IMAGE_TAG=<git-sha 或 latest> ./scripts/deploy-uncloud.sh --registry [context]
#    前提：本机已 docker login ghcr.io（uc deploy 会把凭证传给目标机器）
# =====================================================================
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

REGISTRY_MODE=0
if [ "${1:-}" = "--registry" ]; then
  REGISTRY_MODE=1
  shift
fi
CONTEXT="${1:-automes-local}"
shift || true
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

# uc deploy 在本机用 BuildKit 构建镜像（层缓存 + NuGet 缓存挂载都在本机复用），
# 构建完成后推送镜像并在远端执行健康门禁滚动发布；-y 跳过交互确认。
DEPLOY_FLAGS=(-y)
if [ "$REGISTRY_MODE" = "1" ]; then
  [ -n "${IMAGE_TAG:-}" ] || fail "镜像仓库模式需指定 IMAGE_TAG=<tag>"
  docker manifest inspect "ghcr.io/skybuleli/automotive-mes/mes-api:${IMAGE_TAG}" >/dev/null 2>&1 \
    || fail "GHCR 上找不到 mes-api:${IMAGE_TAG}（检查 IMAGE_TAG 或先 docker login ghcr.io）"
  info "镜像仓库模式：IMAGE_TAG=${IMAGE_TAG}，跳过本机构建"
  DEPLOY_FLAGS+=(--no-build)
fi

info "uc deploy → context: $CONTEXT${1:+ (services: $*)}"
"$UC" deploy -f "$COMPOSE_FILE" -c "$CONTEXT" "${DEPLOY_FLAGS[@]}" "$@"

info "等待 Uncloud Caddy 端口就绪 ..."
for _ in $(seq 1 30); do
  if curl -fsS -o /dev/null "http://localhost:${WEB_PORT}/"; then
    ok "Web 冒烟测试通过: http://localhost:${WEB_PORT}/"
    exit 0
  fi
  sleep 2
done

fail "Web 首页 90 秒内未就绪，请查看: $UC logs -c $CONTEXT mes-web"
