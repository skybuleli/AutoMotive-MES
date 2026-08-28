# AutoMES DevOps 体系（Uncloud + GHCR）

> 目标：内网单/多机轻量部署，零人工干预交付，崩溃可自愈，误操作可回滚。

## 一、全景流水线

```
 开发者 (Mac)
   │ git push
   ▼
 GitHub Actions
   ├─ ci.yml        编译(-warnaserror) + 单测 + Testcontainers 集成测试
   └─ docker.yml    多架构镜像(amd64+arm64, type=gha 缓存) → ghcr.io
                      ghcr.io/skybuleli/automotive-mes/mes-api|mes-web
                      标签: latest + git短sha
   ▼
 部署 (Mac 执行，一条命令)
   ./scripts/deploy-uncloud.sh --registry
   ├─ 1. 部署前 pg_dump 自动备份（pg-backup 容器 → backups/，保留 10 份）
   ├─ 2. uc deploy --no-build：从 GHCR 拉镜像（凭证由 Uncloud 自动转发）
   ├─ 3. 健康门禁滚动发布：新容器 Healthy 后旧容器才移除
   └─ 4. 冒烟测试 + VM 悬空镜像清理
   ▼
 automes-vm2 (Uncloud 集群)
   postgres · pg-backup · mes-api · mes-web · greptimedb · vmalert · alertmanager · caddy
```

本机构建模式（无网/GHCR 不可用时兜底）：`./scripts/deploy-uncloud.sh`
→ 本机 BuildKit 构建（NuGet 缓存挂载）→ Unregistry SSH 推送到 VM。

## 二、Uncloud 0.20 特性使用清单

| 特性 | 用在哪里 | 效果 |
|------|---------|------|
| Compose 部署 + 健康门禁滚动发布 | 主栈 + 观测栈 | 坏版本不再上线；`service_healthy` 依赖真实生效 |
| `x-pre_deploy` 钩子 | 预留（当前迁移在 API 启动时带重试执行） | 需要独立迁移步骤时再启用 |
| Secrets（`secret://` + `x-command`） | POSTGRES_PASSWORD、JWT_SECRET | 密码不进部署计划/命令行；改 `.env.uncloud.local` 重新 deploy 即轮换 |
| Configs | vmalert 规则、alertmanager 配置 | 配置进集群存储；改完重新 deploy 分发（无 bind mount） |
| `x-ports` | Web 15138、GreptimeDB 14000 | 宿主机/局域网访问 |
| `x-caddy` | Web 站点：`:80` 通配 + `encode zstd gzip` + `/_framework` 长缓存 | 任意 Host 可访问；静态资源传输 -70% |
| Unregistry（SSH 推镜像） | 本机构建模式 | 无需 registry 的兜底通道 |
| `uc service exec` | 部署前 pg_dump | 免登 VM 执行运维命令 |
| `uc proxy` | 本地调试 | `uc proxy <service> <port>` 端口转发到本机 |
| `uc service run` | 一次性任务 | `uc service run postgres:17-alpine psql ...`（用完 `uc service rm`） |
| Daemon Prometheus 端点 | 预留 | 各机器 `:51090/metrics`，可抓入观测栈 |
| 健康检查 | API `/health`、Web `/`（busybox wget，chiseled 无 shell/curl） | 部署门禁 + Docker 自愈依据 |

## 三、关键设计决策

1. **chiseled 运行时**：无 shell、非 root、-25% 体积。探活用静态 busybox wget
   （curlimages/curl 是动态 musl，跑不起来）；DataProtection 密钥目录用
   `COPY --chown` 占位文件方案保证命名卷属主正确。
2. **迁移重试在应用内**（`UseMesMigrationsAndSeedAsync`，12 次 × 5s）：VM 重启后
   Docker DNS 就绪竞态不再崩溃循环。权衡：hook 方式需镜像内置 dotnet-ef，不值得。
3. **多架构镜像**：GitHub runner 是 amd64，VM 是 arm64；QEMU 跨构建 + type=gha
   缓存，增量时只有变化层重编。
4. **观测栈独立 compose**：`docker/observability/compose.uncloud.yaml`，
   与主栈解耦部署。OTLP 端点 `http://greptimedb:4000/v1/otlp` 走 Uncloud
   集群 DNS（服务名直连）。

## 四、常用操作

```bash
# 部署（GHCR 镜像）
IMAGE_TAG=latest ./scripts/deploy-uncloud.sh --registry
IMAGE_TAG=1aef3ff ./scripts/deploy-uncloud.sh --registry   # 指定版本

# 回滚：重新部署上一个 sha 标签即可（GHCR 保留全部历史）
IMAGE_TAG=<上一个短sha> ./scripts/deploy-uncloud.sh --registry

# 部署观测栈
uc deploy -f docker/observability/compose.uncloud.yaml -c automes-local -y

# 排查
uc ps -c automes-local                     # 容器/健康状态
uc logs -c automes-local mes-api           # 服务日志
uc service exec -c automes-local postgres -- psql -U mes automes   # 进库
uc proxy -c automes-local greptimedb 4000  # 转发到本机调试

# 密钥轮换：改 .env.uncloud.local → 重新部署（滚动注入新值）
```

## 五、完整 CD（可选进阶）

GitHub Actions 无法直接触达内网 VM（OrbStack NAT）。如需 push 即部署，
在 Mac 上装 GitHub self-hosted runner，注册为 LaunchAgent，监听 main：
`IMAGE_TAG=latest ./scripts/deploy-uncloud.sh --registry`。
当前评估：单人开发 + 内网验收，手动一条命令的收益/复杂度比更高，暂缓。

## 六、轻量方案对比（为什么留在 Uncloud）

| 方案 | 定位 | 不选/选它的原因 |
|------|------|----------------|
| **Uncloud（现用）** | 多机 Docker + WireGuard 组网，CLI 优先 | 无守护进程 UI、无 DB 依赖、secrets/configs/hooks/滚动发布齐全；本仓已全链路落地 |
| Dockge | 单机 Compose 面板 | 只是 UI，无部署流水线、无多机 |
| Komodo | 多机构建+部署+自动化 | 能力重叠但引入 Core/Periphery 两层组件，单机过重 |
| Dokploy | 自托管 PaaS（UI + Git 部署） | 需常驻平台本体（DB+Server），资源占用高于需求 |
| Kamal 2 | CLI 部署（Rails 生态） | 思路与本方案相同（本地构建+SSH 推送），但绑定 Traefik、无集群 secrets/configs |
| Coolify | 全功能 PaaS | 最重；heroku 式体验对内网单应用是负担 |
| k3s + ArgoCD | 轻量 K8s + GitOps | 运维面（Helm/CRD/RBAC）远超当前需求 |

结论：**Uncloud 是该场景的最优解**，缺的只是 CI 多架构镜像与自动化脚本——已补齐。

## 六点五、DevOps 原则对账（2026-08 审计）

| 原则 | 状态 | 说明 |
|------|------|------|
| 无 Dockerfile | **有意偏离** | SDK 容器发布（`dotnet publish /t:PublishContainer`）无法表达本仓必需的自定义步骤：blazor.web.js 补拷贝、chiseled 无 shell 下的 DP 密钥目录属主修正、busybox 探活；且容器发布不产出多平台 manifest（CI 双架构刚需）、不支持 HEALTHCHECK。Dockerfile 本身即声明式配置，维护成本已通过缓存挂载压到最低。重新评估触发条件：引入纯 API 微服务（可 AOT）且放弃 chiseled 细节控制时 |
| 无 Registry（可选） | **符合** | 双通道：日常迭代走本机构建 + Unregistry（`uc deploy` 内建，SSH 直推）；GHCR 仅作为 CI 多架构产物与版本历史仓库。"可选"语义成立 |
| Compose 即部署 | **符合（不含 Aspire）** | Compose 被 Uncloud 直接消费、零 K8s ✓。Aspire 编排**有意不引入**：`aspire publish` 生成的 Compose 无法表达 Uncloud 扩展（x-caddy/x-ports/secrets/configs），反而需要手工 overlay 双轨维护；服务拓扑仅 7 项，收益不抵重构成本 |
| SSH 即权限 | **部分符合（待硬化）** | 现状：日常管理走 Uncloud 客户端证书（WireGuard），SSH 仅 bootstrap；但 VM 仍以 root 运维、CI 不是部署入口（NAT 不可达）。改进路线见下 |
| 小镜像优先 | **部分符合** | Chiseled ✓（241MB 基座 + 应用）。RID 特定发布后 315/325MB（-23%）。**Native AOT 对本应用不可行**：Blazor Server 交互模式与 EF Core 均依赖反射，不支持 AOT；15-100MB 目标仅适用于无 UI 的纯计算微服务。后续可再挖：`-p:PublishTrimmed`（需逐包验证反射安全）、PDB 剔除（牺牲行号诊断，不建议） |

### SSH/权限硬化路线（未实施，按需启用）

1. VM 上建 `deploy` 专用用户（docker 组），禁 root SSH，仅用于镜像清理等运维命令；
2. Mac 上生成部署专钥并 `authorized_keys` 限制（`command=` 前缀只允许 docker prune）；
3. 完整 CD：Mac 装 self-hosted runner（NAT 内唯一可行入口），main 合入自动执行
   `deploy-uncloud.sh --registry`。当前单人开发保持手动门禁是有意选择。

## 七、已知权衡 / 后续

- GHCR 私有转公开已完成；若未来改回私有，本机 `docker login ghcr.io`
  （PAT 需 `read:packages`），Uncloud 会转发凭证到 VM。
- QEMU 跨架构构建较慢（冷构建 arm64 侧 15-30 分钟），GHA 缓存命中后增量分钟级。
  若源仓库转公开，可改用 GitHub 免费 arm64 原生 runner（ubuntu-24.04-arm）
  matrix 提速。
- `backups/` 已加入 .gitignore；异地容灾需另行同步该目录。
