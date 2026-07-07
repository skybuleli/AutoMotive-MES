# Uncloud 集群初始化指南

> **对应任务：** TD.2（初始化）+ TD.3（多机扩展）+ TD.4（部署）+ TD.5（对外暴露）+ TD.7（零停机部署）
> **文档版本：** v5.0 — 2026-07-07（新增 TD.7 零停机滚动部署验证）

## 目录

1. [前置条件](#1-前置条件)
2. [安装 `uc` CLI](#2-安装-uc-cli)
3. [初始化首台服务器](#3-初始化首台服务器)
4. [验证集群状态](#4-验证集群状态)
5. [多机扩展](#5-多机扩展)
6. [部署 MES 服务](#6-部署-mes-服务)
7. [对外暴露 Web 前端](#7-对外暴露-web-前端)
    - [7.1 概述：Uncloud 流量模型](#71-概述uncloud-流量模型)
    - [7.2 默认 `*.uncld.dev` 域名](#72-默认-unclddev-域名)
    - [7.3 `x-ports` 配置详解](#73-x-ports-配置详解)
    - [7.4 自定义域名](#74-自定义域名)
    - [7.5 Caddy HTTPS 行为](#75-caddy-https-行为)
    - [7.6 多域名配置](#76-多域名配置)
    - [7.7 多机环境下的流量路由](#77-多机环境下的流量路由)
    - [7.8 安全加固](#78-安全加固)
    - [7.9 流量监控与排障](#79-流量监控与排障)
    - [7.10 常见问题](#710-常见问题)
8. [运维命令速查](#8-运维命令速查)
9. [故障排查](#9-故障排查)

---

## 1. 前置条件

### 硬件要求（每台服务器）

| 资源 | 最低 | 推荐 |
|------|------|------|
| CPU | 2 核 | 4 核+ |
| 内存 | 4 GB | 8 GB+ |
| 磁盘 | 40 GB SSD | 100 GB+ NVMe |
| 带宽 | 100 Mbps | 1 Gbps |

### 软件要求

| 项目 | 要求 |
|------|------|
| 操作系统 | Ubuntu 22.04+ **或** Debian 12+ |
| 架构 | AMD64（首选）或 ARM64 |
| SSH 访问 | `root` 用户 **或** 具有 `sudo` 免密码权限的用户 |
| 网络 | 公网 IP（或可路由的静态内网 IP）+ 出站互联网访问 |
| 防火墙 | 放行 TCP 端口 `80`、`443`、`51820/udp`（WireGuard） |

### 厂区网络拓扑（建议）

```
┌─────────────────────────────────────────────────────┐
│                  厂区骨干网 (1GbE)                     │
│                                                       │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐           │
│  │ 产线1     │  │ 产线2     │  │ 产线3     │           │
│  │ 服务器    │  │ 服务器    │  │ 服务器    │           │
│  │ 10.0.1.10│  │ 10.0.1.20│  │ 10.0.1.30│           │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘           │
│       │              │              │                  │
│  ┌────┴─────┐  ┌────┴─────┐  ┌────┴─────┐           │
│  │ postgres │  │ mes-api  │  │ mes-web  │           │
│  │   + WAL  │  │ + 服务    │  │ + Caddy  │           │
│  └──────────┘  └──────────┘  └──────────┘           │
│                                                       │
│  Uncloud WireGuard Mesh: 10.100.0.0/24                │
└─────────────────────────────────────────────────────┘
```

> **建议：** 单服务器即可运行全部服务（PostgreSQL + API + Web）。对于高可用，可将 PostgreSQL 独占一台服务器，API 和 Web 分部署其他服务器。

### 环境准备清单

- [ ] 1 台以上 Linux 服务器，满足硬件/软件要求
- [ ] 各服务器 root SSH 密钥已配置（推荐使用 `ssh-keygen` + `ssh-copy-id`）
- [ ] 防火墙已放行 `80`、`443`、`51820/udp` 端口
- [ ] DNS A 记录（可选：为自定义域名配置解析到服务器公网 IP）
- [ ] `.env.production` 文件已准备（参见 `docker/.env.production.example`）

---

## 1. 前置条件

### 硬件要求（每台服务器）

| 资源 | 最低 | 推荐 |
|------|------|------|
| CPU | 2 核 | 4 核+ |
| 内存 | 4 GB | 8 GB+ |
| 磁盘 | 40 GB SSD | 100 GB+ NVMe |
| 带宽 | 100 Mbps | 1 Gbps |

### 软件要求

| 项目 | 要求 |
|------|------|
| 操作系统 | Ubuntu 22.04+ **或** Debian 12+ |
| 架构 | AMD64（首选）或 ARM64 |
| SSH 访问 | `root` 用户 **或** 具有 `sudo` 免密码权限的用户 |
| 网络 | 公网 IP（或可路由的静态内网 IP）+ 出站互联网访问 |
| 防火墙 | 放行 TCP 端口 `80`、`443`、`51820/udp`（WireGuard） |

### 厂区网络拓扑（建议）

```
┌─────────────────────────────────────────────────────┐
│                  厂区骨干网 (1GbE)                     │
│                                                       │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐           │
│  │ 产线1     │  │ 产线2     │  │ 产线3     │           │
│  │ 服务器    │  │ 服务器    │  │ 服务器    │           │
│  │ 10.0.1.10│  │ 10.0.1.20│  │ 10.0.1.30│           │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘           │
│       │              │              │                  │
│  ┌────┴─────┐  ┌────┴─────┐  ┌────┴─────┐           │
│  │ postgres │  │ mes-api  │  │ mes-web  │           │
│  │   + WAL  │  │ + 服务    │  │ + Caddy  │           │
│  └──────────┘  └──────────┘  └──────────┘           │
│                                                       │
│  Uncloud WireGuard Mesh: 10.100.0.0/24                │
└─────────────────────────────────────────────────────┘
```

> **建议：** 单服务器即可运行全部服务（PostgreSQL + API + Web）。对于高可用，可将 PostgreSQL 独占一台服务器，API 和 Web 分部署其他服务器。

### 环境准备清单

- [ ] 1 台以上 Linux 服务器，满足硬件/软件要求
- [ ] 各服务器 root SSH 密钥已配置（推荐使用 `ssh-keygen` + `ssh-copy-id`）
- [ ] 防火墙已放行 `80`、`443`、`51820/udp` 端口
- [ ] DNS A 记录（可选：为自定义域名配置解析到服务器公网 IP）
- [ ] `.env.production` 文件已准备（参见 `docker/.env.production.example`）

---

## 2. 安装 `uc` CLI

在 **本地开发机（macOS/Linux）** 上安装 Uncloud CLI：

### macOS（Homebrew）

```bash
brew install psviderski/tap/uncloud
```

### Linux / macOS（安装脚本）

```bash
curl -fsS https://get.uncloud.run/install.sh | sh
```

### 验证安装

```bash
uc version
# 预期输出：uc version X.Y.Z
```

---

## 3. 初始化首台服务器

### 3.1 单台服务器（最小集群）

将首台服务器初始化为 Uncloud 集群的第一个节点：

```bash
uc machine init root@<服务器公网IP>
```

**执行过程（自动完成）：**

| 步骤 | 说明 |
|------|------|
| 1. SSH 连接 | 通过 SSH 连接到目标服务器 |
| 2. 安装 Docker | 自动安装最新 Docker Engine（如未安装）|
| 3. 安装 `uncloudd` | 安装 Uncloud 守护进程（systemd 服务）|
| 4. 配置 WireGuard | 创建 WireGuard 接口，分配 mesh IP（`10.100.0.0/24`）|
| 5. 部署 Caddy | 自动部署 Caddy 反向代理（监听 `80`/`443`）|
| 6. 注册 DNS | 预留 `*.uncld.dev` 子域名（格式：`<集群名>-<机器名>.uncld.dev`）|

**预期输出：**

```
✓ Machine "server-1" initialized successfully
  Name:      server-1
  IP:        <公网IP>
  Mesh IP:   10.100.0.1/24
  Caddy:     https://automes-server-1.uncld.dev
```

### 3.2 配置本地上下文

初始化后，`uc` 会自动创建本地上下文。验证：

```bash
uc machine ls
# 预期输出类似：
# NAME       IP              MESH IP        OS
# server-1   <公网IP>        10.100.0.1     Ubuntu 24.04
```

---

## 4. 验证集群状态

### 4.1 检查服务运行状态

```bash
# 列出所有部署的服务
uc ps

# 查看各服务的详细状态
uc inspect caddy
```

### 4.2 测试跨机网络

首台服务器初始化完成后，即可进行基本网络测试：

```bash
# 从本地 ping 服务器的 mesh IP
ping 10.100.0.1

# SSH 到服务器，验证 Docker 和 WireGuard
ssh root@<公网IP>
docker ps
# 预期输出：Caddy 容器已在运行

# 查看 WireGuard 状态
uc wg show
```

### 4.3 验证 Caddy HTTPS

浏览器访问 `https://automes-<机器名>.uncld.dev`（或通过 `uc inspect caddy` 获取确切的域名）。

应看到 Caddy 默认欢迎页（`503 Service Unavailable` 为正常，因尚未部署 MES 服务）。

---

## 5. 多机扩展

> 本节对应 **TD.3 多机扩展**。单服务器集群可跳过本节，直接看第 6 节部署。

### 5.1 何时需要多机

| 场景 | 单机是否足够 | 推荐架构 |
|------|-------------|---------|
| 单产线 ≤ 8 设备，日产量 < 500 件 | ✅ 足够 | 全栈单机 |
| 双产线，日产量 500-1000 件 | ⚠️ 视情况 | PostgreSQL 独占一台 + API/Web 另一台 |
| 三产线+，日产量 > 1000 件 | ❌ 需要 | 3+ 台：DB / API / Web 各独立 |
| 需要 HA 高可用 | ❌ 需要 | 至少 2 台，服务分布部署 |

**多机的核心好处：**

- **资源隔离**：PostgreSQL 不争 CPU/内存，避免 Blazor Server 长连接压垮数据库
- **安全隔离**：数据库不暴露在任何对外服务的同一台机器上
- **滚动更新**：多实例可实现零停机部署
- **故障域隔离**：一台机器宕机不影响另一台的核心服务

### 5.2 厂区架构模式

Uncloud 支持通过 `x-machines` 扩展精确定位服务到指定机器。以下是 AutoMES 推荐的两种架构模式：

#### 模式 A：双机部署（推荐用于双产线）

```
┌─ Machine: pg-server ───────────────────┐
│  postgres:17-alpine                     │
│  pg-backup (每日 03:00 备份)             │
│  卷: postgres-data, postgres-wal        │
│  WireGuard Mesh IP: 10.100.0.1/24      │
└─────────────────────────────────────────┘
         ↕  WireGuard 加密隧道（< 1ms）
┌─ Machine: app-server ───────────────────┐
│  mes-api (.NET 10 REST API)             │
│  mes-web (Blazor Server UI)             │
│  Caddy (自动 HTTPS, 对外暴露 80/443)    │
│  WireGuard Mesh IP: 10.100.0.2/24      │
└─────────────────────────────────────────┘
     ↕  Internet
┌─ 浏览器 / 工位终端 ─────────────────────┐
│  https://mes.bosch-esp.com              │
└─────────────────────────────────────────┘
```

**优点：** 数据库完全隔离，安全；API 和 Web 在同一台可减少跨机通信延迟。

#### 模式 B：三机部署（推荐用于三产线+/高可用）

```
┌─ Machine: db-1 ────────────────────────┐
│  postgres:17-alpine                     │
│  pg-backup                              │
│  卷: postgres-data, postgres-wal        │
│  Mesh: 10.100.0.1/24                   │
└─────────────────────────────────────────┘
         ↕  WireGuard
┌─ Machine: api-1 ────────────────────────┐
│  mes-api (.NET 10)                      │
│  scale: 2 (横向扩展)                    │
│  Mesh: 10.100.0.2/24                   │
└─────────────────────────────────────────┘
         ↕  WireGuard
┌─ Machine: web-1 ────────────────────────┐
│  mes-web (Blazor Server)                │
│  Caddy (反向代理 API + Web)             │
│  对外暴露 80/443                        │
│  Mesh: 10.100.0.3/24                   │
└─────────────────────────────────────────┘
     ↕  Internet
┌─ 浏览器 / 工位终端 ─────────────────────┐
│  https://mes.bosch-esp.com              │
└─────────────────────────────────────────┘
```

**优点：** 每层独立扩展；API 可水平扩展多副本。

> **警告：** 三机模式下，`mes-web` 的 Blazor Server SignalR 连接和 `mes-api` 请求都是跨机的。WireGuard 延迟应 < 5ms，否则用户体验会下降。

---

### 5.3 追加新机器

#### 5.3.1 追加第二台机器

```bash
# 给机器一个有意义的名称
uc machine init --name pg-server root@<PostgreSQL 服务器公网IP>
```

**执行过程（自动完成）：**

| 步骤 | 说明 |
|------|------|
| 1. SSH 连接 | 通过 SSH 连接到新服务器 |
| 2. 安装 Docker | 自动安装最新 Docker Engine（如未安装）|
| 3. 安装 `uncloudd` | 安装 Uncloud 守护进程（systemd 服务）|
| 4. WireGuard 对等 | 自动与已有集群节点建立 WireGuard 对等连接 |
| 5. 镜像同步 | 已有服务镜像自动推送到新机器 |
| 6. 更新 Caddy | 如有需要，Caddy 配置自动更新 |

**预期输出：**

```
✓ Machine "pg-server" initialized successfully
  Name:      pg-server
  IP:        <公网IP>
  Mesh IP:   10.100.0.2/24
  Peers:     1 (server-1 @ 10.100.0.1)
  Latency:   0.5ms
```

#### 5.3.2 追加第三台及更多

```bash
uc machine init --name app-server root@<API 服务器IP>
uc machine init --name web-server root@<Web 服务器IP>
```

> **注意：** 初始化新机器时，Uncloud 会自动将已有集群中的所有镜像推送到新机器。如果首次推送量大，可能耗时较长。

#### 5.3.3 验证集群机器列表

```bash
# 列出所有机器
uc machine ls

# 预期输出：
# NAME          IP              MESH IP        OS           DOCKER
# server-1      <IP1>           10.100.0.1     Ubuntu 24.04 27.5.1
# pg-server     <IP2>           10.100.0.2     Ubuntu 24.04 27.5.1
# app-server    <IP3>           10.100.0.3     Ubuntu 24.04 27.5.1
# web-server    <IP4>           10.100.0.4     Ubuntu 24.04 27.5.1

# 查看每台机器的详细信息
uc machine inspect pg-server
# → 显示 CPU/内存/磁盘/网络接口/WireGuard 公钥等
```

---

### 5.4 机器命名约定

为 AutoMES 厂区环境设计的命名规则：

| 机器名 | 用途 | 命名格式 |
|--------|------|---------|
| `db-1` | PostgreSQL 主库 | `{role}-{index}` |
| `api-1` | REST API 实例 1 | `{role}-{index}` |
| `api-2` | REST API 实例 2（水平扩展） | `{role}-{index}` |
| `web-1` | Web 前端（Blazor Server） | `{role}-{index}` |
| `line-1` | 产线 1 边缘服务器 | `line-{lineId}` |
| `line-2` | 产线 2 边缘服务器 | `line-{lineId}` |

> 命名建议：简短、用途明确、统一小写字母+数字，避免特殊字符。

---

### 5.5 使用 `x-machines` 控制服务分布

Uncloud 默认会将服务随机分配到集群中的可用机器。要精确定位服务到指定机器，需要使用 `x-machines` 扩展。

#### 5.5.1 单机约束

```yaml
# docker/compose.yaml — x-machines 示例
services:
  postgres:
    image: postgres:17-alpine
    # PostgreSQL 只运行在 db-1 机器上
    x-machines: db-1
    volumes:
      - postgres-data:/var/lib/postgresql/data

  pg-backup:
    image: postgres:17-alpine
    # 备份服务跟随 PostgreSQL
    x-machines: db-1
    depends_on:
      postgres:
        condition: service_healthy

  mes-api:
    build:
      context: ..
      dockerfile: src/MesAdmin.Api/Dockerfile
    # API 只运行在 api-1 上
    x-machines: api-1
    depends_on:
      postgres:
        condition: service_healthy

  mes-web:
    build:
      context: ..
      dockerfile: src/MesAdmin.Web/Dockerfile
    # Web 只运行在 web-1 上
    x-machines: web-1
    depends_on:
      mes-api:
        condition: service_healthy
```

#### 5.5.2 多机分布（水平扩展 API）

```yaml
  mes-api:
    build:
      context: ..
      dockerfile: src/MesAdmin.Api/Dockerfile
    # API 分布在 api-1 和 api-2 两台机器上
    x-machines:
      - api-1
      - api-2
    # 每台机器运行 1 个副本（共 2 个）
    scale: 2
```

> **注意：** 多副本 API 扩展需要 SignalR Redis 背板支持跨实例消息同步（TD.4 中配置）。当前阶段单实例即可。

#### 5.5.3 全局服务（每台机器一个副本）

```yaml
  # 全局 Caddy 反向代理（Uncloud 自动部署，无需手动配置）
  # 如需自定义全局部署，使用 x-machines: --global
```

#### 5.5.4 使用环境变量动态控制分布

```yaml
  mes-api:
    x-machines: ${API_MACHINES:-api-1}
```

通过 `-e API_MACHINES="api-1,api-2"` 参数在部署时动态指定。

---

### 5.6 跨机卷管理

Uncloud 的卷是**机器绑定的**：卷在创建时绑定到特定机器，不能跨机器自动迁移。

#### 5.6.1 卷与 `x-machines` 的关联

```yaml
volumes:
  postgres-data:    # 这个卷会在 db-1 上创建
  postgres-wal:     # 这个卷会在 db-1 上创建
  pg-backup-data:   # 这个卷会在 db-1 上创建
```

**规则：**

- 卷会在其首个引用服务所部署的机器上创建
- 如果服务被 `x-machines` 绑定到具体机器，卷也会在该机器上创建
- 卷不能跨机器共享，服务必须部署在有卷的机器上

#### 5.6.2 查看卷列表

```bash
# 列出所有卷及其所在的机器
uc volume ls

# 预期输出：
# NAME             SIZE       MACHINE   STATUS
# postgres-data    2.1 GiB    db-1      active
# postgres-wal     845 MiB    db-1      active
# pg-backup-data   156 MiB    db-1      active
```

#### 5.6.3 手动创建卷

```bash
# 在指定机器上创建卷
uc volume create my-volume -m db-1
```

#### 5.6.4 卷数据迁移

如果需要将 PostgreSQL 迁移到另一台机器：

```bash
# 1. 在旧机器上导出数据
uc exec postgres -- pg_dump -Fc -U mes -d automes > /tmp/automes-backup.dump

# 2. 将备份文件复制到新机器
uc cp /tmp/automes-backup.dump db-2:/tmp/

# 3. 在目标机器上创建新卷
uc volume create postgres-data -m db-2

# 4. 更新 compose.yaml，将 postgres 的 x-machines 改为 db-2
# 5. 重新部署
uc deploy -f docker/compose.yaml -e .env.production
```

---

### 5.7 网络拓扑验证

初始化多台机器后，应验证跨机网络连接正常。

#### 5.7.1 WireGuard 对等状态

```bash
# 查看全局 WireGuard 状态
uc wg show

# 预期输出：
# PEER              ENDPOINT            HANDSHAKE   LATENCY
# db-1              <IP1>:51820         12s ago     0.5ms
# api-1             <IP2>:51820         15s ago     0.8ms
# web-1             <IP3>:51820         10s ago     0.3ms

# 查看指定机器的对等详情
uc wg show --machine db-1
# → 显示 db-1 上所有对等连接的详细信息
```

#### 5.7.2 跨机延迟测试

```bash
# 从一台机器 ping 另一台的 mesh IP
uc exec db-1 -- ping -c 5 10.100.0.3  # ping web-1 的 mesh IP

# 预期结果（同一厂区交换机）：
# rtt min/avg/max/mdev = 0.123/0.256/0.456/0.089 ms

# 如果延迟 > 5ms，检查网络链路或考虑将频繁通信的服务放在同一台机器上
```

#### 5.7.3 跨机服务发现验证

```bash
# 从 app-server 上测试连接 PostgreSQL
uc exec app-server -- nc -zv postgres 5432
# Expected: Connection to postgres port 5432 succeeded!

# 从 web-server 上测试连接 API
uc exec web-server -- curl -f http://mes-api:5040/health
# Expected: {"status":"healthy","service":"MesAdmin.Api","timestamp":"..."}
```

#### 5.7.4 带宽测试

> 厂区环境下，跨机通信如有大量数据流转（如 Blazor Server SignalR + OEE 实时推送），建议测试带宽：

```bash
# 使用 iperf3 测试跨机带宽
uc exec app-server -- iperf3 -c 10.100.0.1  # 测试到 db-1 的带宽
# 预期：>= 1 Gbps（同一交换机）
```

---

### 5.8 优雅移除机器

当需要从集群中移除一台机器（例如退役、维护）：

#### 5.8.1 移除前检查

```bash
# 查看该机器上运行的服务
uc ps | grep <machine-name>

# 查看该机器上的卷
uc volume ls | grep <machine-name>
```

#### 5.8.2 迁移服务

1. **更新 compose.yaml**：将服务的 `x-machines` 改为其他机器
2. **重新部署**：`uc deploy -f docker/compose.yaml -e .env.production`
3. **验证新服务已启动**：`uc ps`

#### 5.8.3 移除机器

```bash
# 从集群中移除机器
uc machine rm <machine-name>

# 预期：Uncloud 会自动清理 WireGuard 配置和 Caddy 路由
# 但不会自动删除该机器上的 Docker 卷（数据安全）

# 手动清理残留数据（如有需要）
ssh root@<服务器IP>
docker volume prune -f
docker system prune -f
```

> ⚠️ **安全警告：** 移除机器前，确保其上的卷数据已备份或迁移。卷删除不可恢复！

---

### 5.9 跨机故障处理

Uncloud 采用**去中心化设计**，无控制平面。这意味着：

- 一台机器宕机，**不会影响**其他机器的正常运行
- 宕机机器上的服务**不会自动迁移**到其他机器（与 K8s 不同）
- 服务恢复需要在机器恢复后手动或自动重启

#### 5.9.1 机器宕机检测

```bash
# 查看机器状态
uc machine ls
# 宕机的机器状态会显示为 "offline"

# 查看 WireGuard 对等状态
uc wg show --machine db-1
# 宕机的机器会显示 "HANDSHAKE" 超时
```

#### 5.9.2 故障机器恢复

```bash
# 1. 修复服务器（硬件/网络）
# 2. SSH 连接后，uncloudd 会自动重连集群
uc machine inspect db-1
# 状态应恢复为 "online"

# 3. 检查服务是否自动重启
uc ps | grep postgres
# Uncloud 会将容器自动重启（restart: unless-stopped）

# 4. 手动启动未自动恢复的服务
uc deploy -f docker/compose.yaml -e .env.production
```

#### 5.9.3 跨机日志聚合

```bash
# 跨机聚合所有服务日志
uc logs -f

# 跨机聚合指定服务日志
uc logs -f mes-api

# 查看指定机器的所有日志
ssh root@<机器IP>
journalctl -u uncloudd -f --since "5 min ago"
```

#### 5.9.4 滚动更新（零停机）

多机环境下，`uc deploy` 默认执行零停机滚动更新：

```yaml
services:
  mes-api:
    # 多副本确保更新时至少一个实例在线
    x-machines:
      - api-1
      - api-2
    scale: 2
```

```bash
# 更新服务（零停机）
uc deploy -f docker/compose.yaml -e .env.production

# 执行流程：
# 1. 在 api-2 上启动新版本容器
# 2. 等待健康检查通过
# 3. 将流量路由到 api-2
# 4. 停止 api-1 上的旧版本容器
# 5. 在 api-1 上启动新版本容器
# 6. 恢复负载均衡
```

> **注意：** 零停机需要每个服务至少 2 个副本分布在不同的机器上。单副本服务在更新期间会有短暂停机。

#### 5.9.5 备用机器策略

对于生产环境，建议保留一台备用机器：

```bash
# 备用机器不部署任何生产服务
uc machine init --name standby-1 root@<备用服务器IP>
# 在需要时，通过更新 x-machines 快速切换
```

---

## 6. 部署 MES 服务

> 本节对应 **TD.4 `uc deploy` 生产部署**。

### 6.1 工作流程总览

`uc deploy` 是单命令部署入口，内部串联以下步骤：

```
┌─────────┐   ┌─────────┐   ┌──────────┐   ┌──────────┐   ┌───────────┐
│ 加载    │ → │ 构建    │ → │ 推送镜像  │ → │ 计划部署  │ → │ 执行部署   │
│ compose │   │ 镜像    │   │ 到集群    │   │ 预览+确认 │   │ (零停机)   │
│ .yaml   │   │ (本地)  │   │ 各机器    │   │          │   │           │
└─────────┘   └─────────┘   └──────────┘   └──────────┘   └───────────┘
```

您也可以将构建和部署分离为独立步骤：

```bash
# 步骤 1：构建镜像并推送到集群
uc build --push -f docker/compose.yaml

# 步骤 2：仅部署（不构建）
uc deploy --no-build -f docker/compose.yaml -e .env.production
```

默认的 `uc deploy` 会根据 `build:` 部分的 Dockerfile 构建镜像，使用 Git 版本自动标记（格式：`<项目>/<服务>:<日期>.<短SHA>`），并将镜像推送到集群中所有相关的机器。

---

### 6.2 准备环境变量

#### 6.2.1 创建环境变量文件

```bash
# 复制模板
cp docker/.env.production.example .env.production

# 编辑填入生产值
vim .env.production
```

**关键变量（必须设置）：**

| 变量 | 生成命令 | 说明 |
|------|---------|------|
| `POSTGRES_PASSWORD` | `openssl rand -base64 24` | PostgreSQL 数据库密码 |
| `JWT_SECRET` | `openssl rand -base64 32` | JWT 签名密钥（≥256 位）|

**可选变量：**

| 变量 | 默认值 | 说明 |
|------|--------|------|
| `SAP_USE_REAL_CLIENT` | `false` | 是否启用真实 SAP 连接 |
| `SMTP_ENABLED` | `false` | 是否启用邮件推送 |
| `OTLP_ENDPOINT` | `http://greptimedb:4000/v1/otlp` | 可观测性端点 |

#### 6.2.2 通过 `uc env` 注入环境变量（推荐生产方式）

除了 `.env` 文件，Uncloud 支持通过 `uc env` 命令直接注入变量到部署上下文，避免敏感信息写入文件系统：

```bash
# 设置单个变量
uc env set POSTGRES_PASSWORD <值>

# 从文件加载
uc env load < .env.production

# 查看已设置的变量
uc env ls

# 移除变量
uc env rm POSTGRES_PASSWORD

# 部署时自动使用已设置的 uc env 变量
uc deploy -f docker/compose.yaml
# 无需 -e 参数，uc 会自动注入已设置的环境变量
```

> **建议：** 首次部署使用 `-e .env.production`，生产稳定后改为 `uc env set` 方式管理密钥。脚本化部署时，建议从 CI/CD 密钥管理的变量动态写入，不持久化到磁盘。

---

### 6.3 首次部署

#### 6.3.1 执行部署命令

```bash
# 从项目根目录执行（compose.yaml 在 docker/ 下）
uc deploy -f docker/compose.yaml -e .env.production
```

#### 6.3.2 部署计划预览

`uc deploy` 会首先输出一个 **Terraform 风格的计划**，列出所有即将创建/更新的资源：

```
▶ Planning deployment...

  Service: postgres
    Image:      postgres:17-alpine
    Machine:    server-1 (x-machines: all)
    Ports:      127.0.0.1:5432:5432
    Volumes:    postgres-data → /var/lib/postgresql/data  [create]
                postgres-wal  → /var/lib/postgresql/wal_archive  [create]
    Health:     ✓ (CMD-SHELL pg_isready)
    Restart:    unless-stopped

  Service: pg-backup
    Image:      postgres:17-alpine
    Machine:    server-1 (depends on postgres)
    Volumes:    pg-backup-data → /backups  [create]
    Depends:    postgres: service_healthy

  Service: mes-api
    Build:      src/MesAdmin.Api/Dockerfile
    Image:      automes/mes-api:20260707.1234.abc1234
    Machine:    server-1 (x-machines: all)
    Ports:      127.0.0.1:5040:5040
    Depends:    postgres: service_healthy
    Health:     ✓ (CMD curl -f http://localhost:5040/health)

  Service: mes-web
    Build:      src/MesAdmin.Web/Dockerfile
    Image:      automes/mes-web:20260707.1234.abc1234
    Machine:    server-1 (depends on mes-api)
    Ports:      80:5138
    URL:        https://automes-<id>.uncld.dev
    Depends:    mes-api: service_healthy
    Health:     ✓ (CMD curl -f http://localhost:5138/)

  Volumes to create: 3
    - postgres-data
    - postgres-wal
    - pg-backup-data

▶ Plan approved. Proceed with deployment? (Y/n):
```

> 仔细审核计划中的每项内容，特别是**端口映射**、**卷创建**和**镜像标签**。确认无误后输入 `Y`。

#### 6.3.3 部署执行

确认后，`uc deploy` 按顺序执行：

```
▶ Deploying...
  ✓ postgres: image pulled (17-alpine), container started
  ✓ postgres: healthcheck passed (3.2s)
  ✓ pg-backup: container started
  ✓ mes-api: building image... (45.2s)
  ✓ mes-api: image pushed to server-1
  ✓ mes-api: container started
  ✓ mes-api: healthcheck passed (2.1s)
  ✓ mes-web: building image... (38.7s)
  ✓ mes-web: image pushed to server-1
  ✓ mes-web: container started
  ✓ mes-web: healthcheck passed (4.5s)
  ✓ Service "mes-web" published at: https://automes-abc123.uncld.dev

▶ All services deployed successfully. (total: 2m 34s)
```

**时间预估：**

| 阶段 | 首次部署 | 后续更新 |
|------|---------|---------|
| 拉取 PostgreSQL 镜像 | 15-30s | < 5s（缓存）|
| 构建 mes-api 镜像 | 30-90s | 15-30s（层缓存）|
| 构建 mes-web 镜像 | 30-60s | 15-30s（层缓存）|
| 镜像推送 | 10-30s | 5-10s（增量层）|
| 容器启动 + 健康检查 | 15-30s | 15-30s |
| **总计** | **2-4 min** | **1-2 min** |

---

### 6.4 部署计划详解

部署计划是理解 `uc deploy` 做了什么的关键。以下是如何阅读计划中的关键信息：

#### 服务级信息

| 字段 | 含义 | 示例值 |
|------|------|--------|
| `Image` | 使用的镜像（预构建 / 从源码构建）| `postgres:17-alpine` / `automes/mes-api:20260707.1234.abc` |
| `Machine` | 部署到哪台机器 | `server-1` / `all` / `x-machines` 约束 |
| `Ports` | 端口映射 | `127.0.0.1:5040:5040` |
| `Volumes` | 卷挂载 | `postgres-data → /var/lib/postgresql/data [create]` |
| `Health` | 健康检查 | `✓ (CMD curl ...)` |
| `URL` | 对外暴露的 URL（如有） | `https://automes-abc.uncld.dev` |
| `Depends` | 依赖关系 | `postgres: service_healthy` |

#### 状态指示器

| 符号 | 含义 |
|------|------|
| `[create]` | 将创建新资源（卷/服务）|
| `[update]` | 将更新现有资源 |
| `[remove]` | 将移除现有资源 |
| `[no change]` | 资源无变化 |

---

### 6.5 构建与推送镜像

#### 6.5.1 默认行为

默认情况下，`uc deploy` 会自动为每个包含 `build:` 的服务构建 Docker 镜像。镜像标签使用 Git 信息自动生成：

```
# 格式：<项目>/<服务>:<Git日期>.<短SHA>
automes/mes-api:20260707.1234.abc1234
```

如果工作目录不是 Git 仓库，则使用本地日期/时间。

#### 6.5.2 自定义镜像标签

可以通过 `image` 属性自定义镜像命名规则：

```yaml
services:
  mes-api:
    build:
      context: ..
      dockerfile: src/MesAdmin.Api/Dockerfile
    # 自定义镜像标签格式
    image: automes/mes-api:{{gitdate "20060102"}}.{{gitsha 7}}
```

**模板变量参考：**

| 变量 | 说明 | 示例 |
|------|------|------|
| `{{gitdate "20060102"}}` | Git 提交日期 | `20260707` |
| `{{gitsha 7}}` | Git 提交 SHA（前 N 位）| `abc1234` |
| `{{if .Git.IsDirty}}.dirty{{end}}` | 是否有未提交更改 | `.dirty` |
| `$ENV_VAR` | 环境变量 | `${GITHUB_RUN_ID}` |

#### 6.5.3 分步构建与部署（CI/CD 场景）

```bash
# 步骤 1：构建镜像并推送到集群机器
uc build --push -f docker/compose.yaml

# 步骤 2：查看已推送的镜像
uc images

# 步骤 3：部署（跳过构建步骤）
uc deploy --no-build -f docker/compose.yaml -e .env.production
```

#### 6.5.4 构建参数

```bash
# 传递构建参数
uc deploy --build-arg BUILD_CONFIG=Release

# 在 compose.yaml 中指定构建参数
services:
  mes-api:
    build:
      context: ..
      dockerfile: src/MesAdmin.Api/Dockerfile
      args:
        BUILD_CONFIG: Release
```

#### 6.5.5 多平台构建

如果厂区同时有 AMD64 和 ARM64 服务器：

```yaml
services:
  mes-api:
    build:
      context: ..
      dockerfile: src/MesAdmin.Api/Dockerfile
      platforms:
        - linux/amd64
        - linux/arm64
```

> **注意：** 多平台构建需在 Docker 中启用 containerd 镜像存储。

---

### 6.6 部署后验证清单

部署完成后，必须执行以下验证：

#### 6.6.1 基础检查

```bash
# 1. 列出所有已部署的服务
uc ls
# 确认所有服务状态为 Running

# 2. 检查每个服务的详细信息
uc inspect postgres
uc inspect mes-api
uc inspect mes-web
# 确认健康检查通过、端口映射正确、运行的机器正确

# 3. 检查容器日志无错误
uc logs mes-api | grep -i error | tail -10
uc logs postgres | grep -i error | tail -10
```

#### 6.6.2 API 健康检查

```bash
# 4. 测试 API 健康端点
curl -f https://automes-<id>.uncld.dev/health
# 预期：{"status":"healthy","service":"MesAdmin.Api","timestamp":"..."}
```

#### 6.6.3 Web UI 检查

```bash
# 5. 浏览器访问
open https://automes-<id>.uncld.dev
# 预期：显示登录页面（/login）
```

#### 6.6.4 数据库验证

```bash
# 6. 验证 PostgreSQL 已就绪，Migration 已自动执行
uc exec postgres -- psql -U mes -d automes -c "\\dt"
# 预期：显示所有已创建的表

# 7. 验证种子数据已写入
uc exec postgres -- psql -U mes -d automes -c "SELECT COUNT(*) FROM production_orders;"
```

#### 6.6.5 网络穿透验证

```bash
# 8. 从 Web 容器测试到达 API
uc exec mes-web -- curl -f http://mes-api:5040/health
# 预期：跨机通信正常，返回健康状态

# 9. 从 API 容器测试到达 PostgreSQL
uc exec mes-api -- nc -zv postgres 5432
# 预期：Connection succeeded
```

#### 6.6.6 一键验证脚本

可以编写一个简单的验证脚本：

```bash
#!/bin/bash
# validate-deploy.sh
set -euo pipefail

DOMAIN="${1:?用法: $0 <域名>}"

echo "=== 验证部署：${DOMAIN} ==="

# API 健康
echo -n "API Health: "
curl -sf "https://${DOMAIN}/health" && echo "✅" || echo "❌"

# Web 页面
echo -n "Web UI: "
curl -sf -o /dev/null "https://${DOMAIN}/" && echo "✅" || echo "❌"

# 服务列表
echo "=== 服务状态 ==="
uc ls

echo "=== 完成 ==="
```

---

### 6.7 更新部署

当代码或配置发生变更后，需要更新已部署的服务。

#### 6.7.1 标准更新流程

```bash
# 1. 确保工作目录干净（所有变更已提交）
# 2. 部署更新
uc deploy -f docker/compose.yaml -e .env.production
```

Uncloud 会：
1. 重新构建有 `build:` 的服务的镜像
2. 使用新 Git SHA 标记新镜像
3. 执行**零停机滚动更新**（如果服务有多副本）
4. 先启动新容器，等待健康检查通过
5. 然后移除旧容器

#### 6.7.2 仅配置更新（不构建镜像）

如果只修改了 `compose.yaml` 中的配置（如环境变量、端口映射），不需要重新构建镜像：

```bash
# 1. 先构建并推送最新镜像（确保集群上有最新版本）
uc build --push -f docker/compose.yaml

# 2. 仅应用配置变更
uc deploy --no-build -f docker/compose.yaml -e .env.production
```

> **注意：** 如果使用默认的动态 Git 标签，`--no-build` 部署可能因标签不匹配失败。建议使用固定标签或先 build --push。

#### 6.7.3 跨机器镜像同步

```bash
# 手动推送本地镜像到指定机器
uc image push automes/mes-api:latest -m db-1,api-1

# 查看机器上的镜像列表
uc images
```

---

### 6.8 回滚部署

Uncloud 不提供原生的 `uc rollback` 命令。回滚需要通过重新部署旧版本实现。

#### 6.8.1 Git 回滚法（推荐）

```bash
# 1. 找到上一个稳定版本的 Git 提交哈希
git log --oneline -5
# abc1234 feat: add SPC quality module
# def5678 feat: fix production order saga

# 2. 回滚到该版本
git checkout def5678

# 3. 重新部署
uc deploy -f docker/compose.yaml -e .env.production

# 4. 验证
curl -f https://automes-<id>.uncld.dev/health

# 5. 修复问题后重新提交
# 回到 main 分支进行修复
git checkout main
# ... 修复代码 ...
git commit -m "fix: ..."
uc deploy -f docker/compose.yaml -e .env.production
```

#### 6.8.2 手动指定旧镜像标签

```bash
# 1. 查看可用的镜像版本
uc images | grep mes-api
# automes/mes-api:20260707.1234.abc1234
# automes/mes-api:20260706.0900.def5678  ← 上一个版本

# 2. 在 compose.yaml 中指定旧版本
services:
  mes-api:
    image: automes/mes-api:20260706.0900.def5678  # 使用固定标签
    pull_policy: never  # 避免从 registry 拉取（镜像已在集群上）

# 3. 部署（不构建）
uc deploy --no-build -f docker/compose.yaml -e .env.production
```

#### 6.8.3 快速回滚记录

建议在每次部署时记录当前版本信息，便于快速回滚：

```bash
# 部署前记录版本
DEPLOY_TAG=$(date +%Y%m%d-%H%M%S)
echo "Deploy $DEPLOY_TAG: $(git rev-parse HEAD)" >> deploy-log.txt

# 部署时使用固定标签
uc build --push -f docker/compose.yaml
docker tag automes/mes-api:latest automes/mes-api:$DEPLOY_TAG
uc image push automes/mes-api:$DEPLOY_TAG
```

---

### 6.9 水平扩展服务

#### 6.9.1 扩展 API 副本

```yaml
services:
  mes-api:
    build:
      context: ..
      dockerfile: src/MesAdmin.Api/Dockerfile
    # 部署到 api-1 和 api-2 两台机器
    x-machines:
      - api-1
      - api-2
    # 每台机器 1 个副本，共 2 个
    scale: 2
```

```bash
# 部署扩展后的配置
uc deploy -f docker/compose.yaml -e .env.production

# 查看副本分布
uc ls
# mes-api  | Running  | replicas: 2/2  | api-1, api-2
```

> ⚠️ **API 扩展前置条件：** 多副本 API 需要 SignalR Redis 背板来同步 WebSocket 消息。否则 OEE/Andon 实时推送仅发送到连接了对应 API 实例的客户端。单实例生产环境当前无需此配置。

#### 6.9.2 扩展 PostgreSQL（只读副本）

PostgreSQL 的水平扩展超出 `compose.yaml` 的能力范围。如需读写分离，建议：

- 使用 PostgreSQL 原生流复制配置 Standby 节点
- 或使用 Patroni + etcd 管理 PostgreSQL HA
- 这些超出 TD 范围，详见 TD.6 备份策略

---

### 6.10 CI/CD 集成

#### 6.10.1 GitHub Actions 工作流示例

在 `.github/workflows/deploy.yml` 中：

```yaml
name: Deploy to Uncloud

on:
  push:
    branches: [main]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Install uc CLI
        run: curl -fsS https://get.uncloud.run/install.sh | sh

      - name: Set up Uncloud context
        run: |
          uc ctx import <<< "${{ secrets.UNCLOUD_CTX }}"
          uc ctx use production

      - name: Deploy
        run: |
          uc deploy -f docker/compose.yaml \
            -e <(echo "${{ vars.ENV_PRODUCTION }}")
```

**前置准备：**

```bash
# 在本地导出 Uncloud 上下文（包含认证信息）
uc ctx export > uncloud-ctx.json
# 将内容添加到 GitHub Secrets：UNCLOUD_CTX

# 将 .env.production 内容添加到 GitHub Variables：ENV_PRODUCTION
```

#### 6.10.2 本地 CI 脚本

```bash
#!/bin/bash
# deploy-ci.sh
set -euo pipefail

ENV_FILE="${1:-.env.production}"

echo "=== [CI] 部署到 Uncloud ==="

# 检查上下文
echo "=== 1/4: 检查集群 ==="
uc machine ls

# 构建并推送
echo "=== 2/4: 构建镜像 ==="
uc build --push -f docker/compose.yaml

# 部署
echo "=== 3/4: 部署 ==="
uc deploy --no-build -f docker/compose.yaml -e "$ENV_FILE"

# 验证
echo "=== 4/4: 验证 ==="
sleep 10
DOMAIN=$(uc inspect mes-web --format '{{(index .Ports 0).URL}}')
curl -sf "https://$DOMAIN/health" && echo "✅ 部署成功" || echo "❌ 部署失败"
```

---

### 6.11 多机部署（x-machines）

如果按 TD.3 配置了多台机器，需要在 `compose.yaml` 中添加 `x-machines` 约束来控制服务分布。

#### 6.11.1 双机模式部署

```yaml
services:
  postgres:
    image: postgres:17-alpine
    x-machines: db-1           # PostgreSQL 独占 db-1
    volumes:
      - postgres-data:/var/lib/postgresql/data
    # ...

  pg-backup:
    image: postgres:17-alpine
    x-machines: db-1           # 备份跟随数据库
    # ...

  mes-api:
    build:
      context: ..
      dockerfile: src/MesAdmin.Api/Dockerfile
    x-machines: app-1          # API + Web 同机
    # ...

  mes-web:
    build:
      context: ..
      dockerfile: src/MesAdmin.Web/Dockerfile
    x-machines: app-1          # 与 API 同机，减少跨机延迟
    # ...
```

```bash
# 部署（uc 自动将服务部署到指定机器）
uc deploy -f docker/compose.yaml -e .env.production
```

#### 6.11.2 跨机镜像推送优化

当 `x-machines` 指定了部分机器时，`uc deploy` 和 `uc build --push` 只将镜像推送到这些机器上，节省带宽和推送时间。

```bash
# 只推送到 db-1 和 app-1（x-machines 指定的机器）
uc build --push -f docker/compose.yaml
```

---

### 6.12 生产部署检查清单

部署到生产环境前，请逐项确认：

#### 基础设施

- [ ] 所有服务器满足硬件要求（CPU、内存、磁盘）
- [ ] 防火墙已放行 `80`、`443`、`51820/udp` 端口
- [ ] SSH 密钥已配置到所有服务器
- [ ] WireGuard mesh 延迟 < 5ms（同机房）或 < 20ms（跨机房）
- [ ] 跨机服务发现正常（`uc exec app-1 -- curl http://postgres:5432`）

#### 配置

- [ ] `.env.production` 中的所有变量已正确设置
- [ ] `POSTGRES_PASSWORD` 已使用强密码（openssl rand -base64 24）
- [ ] `JWT_SECRET` 已使用 256 位密钥（openssl rand -base64 32）
- [ ] `SAP_USE_REAL_CLIENT=false`（除非已配置 SAP）
- [ ] `SMTP_ENABLED=false`（除非已配置 SMTP）

#### 服务

- [ ] 所有服务的健康检查已通过
- [ ] API `/health` 端点返回 `healthy`
- [ ] Web UI 可正常访问并显示登录页面
- [ ] PostgreSQL Migration 已自动执行
- [ ] 种子数据已写入
- [ ] 日志中无 `ERROR` 级别条目

#### Dockerfiles

- [ ] `src/MesAdmin.Api/Dockerfile` 最新（多阶段构建）
- [ ] `src/MesAdmin.Web/Dockerfile` 最新（多阶段构建）
- [ ] `.dockerignore` 已排除不需要的文件
- [ ] 非 root 用户运行（`USER $APP_UID`）

#### 安全

- [ ] `mes-api` 端口绑定了 `127.0.0.1`（仅宿主机本机可访问）
- [ ] `postgres` 端口绑定了 `127.0.0.1`（仅宿主机本机可访问）
- [ ] Caddy 自动 HTTPS 已生效
- [ ] 未暴露 `mes-api` 或 `postgres` 到公网

#### 备份

- [ ] `pg-backup` 服务已启动并正常运行
- [ ] `postgres-data` 卷已创建
- [ ] `postgres-wal` 卷已创建
- [ ] `pg-backup-data` 卷已创建

---

### 6.13 运维命令

```bash
# ── 部署相关 ──
uc deploy -f compose.yaml -e .env       # 部署/更新服务
uc build --push -f compose.yaml          # 构建并推送镜像
uc deploy --no-build -f compose.yaml     # 仅部署（不构建）

# ── 服务管理 ──
uc ls                                    # 列出所有服务
uc inspect <service>                     # 查看服务详情
uc ps                                    # 列出运行中的容器
uc logs <service>                        # 查看日志
uc logs -f                               # 实时日志流
uc rm <service>                          # 移除服务

# ── 镜像管理 ──
uc images                                # 查看集群上的所有镜像
uc image push <image:tag> -m <machine>   # 手动推送镜像到指定机器

# ── 环境变量 ──
uc env set <key>=<value>                 # 设置环境变量
uc env ls                                # 列出环境变量
uc env rm <key>                          # 移除环境变量

# ── 卷管理 ──
uc volume ls                             # 列出所有卷
uc volume create <name> -m <machine>     # 在指定机器上创建卷
uc volume rm <name>                      # 移除卷

# ── 部署计划 ──
# 查看当前部署的详细计划（不实际部署）
uc deploy -f compose.yaml --dry-run
```

---

## 7. 对外暴露 Web 前端

> 本节对应 **TD.5 对外暴露配置**。

### 7.1 概述：Uncloud 流量模型

理解 Uncloud 的流量路由模型是正确配置对外暴露的基础。

```
                          ┌──────────────┐
                          │   Internet    │
                          │  (浏览器/用户) │
                          └──────┬───────┘
                                 │ HTTPS :443
                                 ▼
                          ┌──────────────┐
                          │  Caddy 全局   │
                          │  反向代理     │
                          │  (自动部署)    │
                          │  Let's Encrypt│
                          └──────┬───────┘
                                 │ HTTP :5138 (内部)
                                 ▼
                          ┌──────────────┐
                          │   mes-web    │
                          │ Blazor Server│
                          │  :5138       │
                          └──────┬───────┘
                                 │ SignalR / HTTP
                                 ▼
                          ┌──────────────┐
                          │   mes-api    │
                          │  REST API    │
                          │  :5040       │
                          └──────┬───────┘
                                 │ Npgsql
                                 ▼
                          ┌──────────────┐
                          │  postgres    │
                          │  :5432       │
                          └──────────────┘
```

**关键要点：**

- **只有 `mes-web` 需要对外暴露** — 它通过端口 `80` 被 Caddy 反向代理，用户通过 HTTPS 访问
- **`mes-api` 和 `postgres` 不得对外暴露** — 它们只通过 Docker 内部网络或 WireGuard mesh 可达
- **Caddy 由 Uncloud 自动管理** — 每个集群节点上自动部署一个 Caddy 全局服务，监听 `80`/`443`
- **`x-ports` 是声明式配置** — 在 `compose.yaml` 中声明需要对外暴露的端口，Caddy 自动配置反向代理

---

### 7.2 默认 `*.uncld.dev` 域名

#### 7.2.1 自动分配

部署后，Uncloud 自动为配置了 `x-ports` 的服务分配一个 `*.uncld.dev` 子域名。这是 Uncloud 的免费 DNS 服务，无需任何手动 DNS 配置。

```bash
# 查看对外暴露的服务及域名
uc inspect mes-web

# 输出示例：
# Service: mes-web
#   Status:     Running
#   Image:      automes/mes-web:20260707.1234.abc1234
#   Ports:      80 → 5138
#   URL:        https://automes-abc123.uncld.dev
#   Machines:   server-1
```

#### 7.2.2 域名格式

```
https://<集群ID>-<服务名>.<机器名>.uncld.dev
https://automes-abc123-server-1.uncld.dev
```

> 集群 ID 是自动生成的唯一标识符，在 `uc machine init` 时分配。

#### 7.2.3 快速验证

```bash
# 获取对外暴露的 URL
UC_URL=$(uc inspect mes-web --format '{{(index .Ports 0).URL}}')
echo "MES URL: $UC_URL"

# 测试 HTTPS 访问
curl -sf "$UC_URL/health" | head -1
# 预期：{"status":"healthy",...}
```

---

### 7.3 `x-ports` 配置详解

`x-ports` 是 Uncloud 的 Compose 扩展，用于声明服务端口应该对外暴露的方式。

#### 7.3.1 基本语法

```yaml
services:
  mes-web:
    x-ports:
      - <域名>:<容器端口>/<协议>
```

#### 7.3.2 AutoMES 配置

当前 `docker/compose.yaml` 中 `mes-web` 的配置：

```yaml
services:
  mes-web:
    ports:
      - "80:5138"          # Docker 层：将容器 5138 映射到宿主机 80
    # x-ports 暂未显式配置，Uncloud 自动处理
```

> **当前行为：** 由于 `x-ports` 未显式配置，Uncloud 部署时自动为端口 `80` 分配 `*.uncld.dev` 域名。

#### 7.3.3 显式配置 `x-ports`（推荐）

显式配置可以提供更多控制：

```yaml
services:
  mes-web:
    ports:
      - "80:5138"
    x-ports:
      # 使用默认 uncld.dev 域名
      - "80"
```

#### 7.3.4 完整配置格式

```yaml
services:
  mes-web:
    ports:
      - "80:5138"
    x-ports:
      # 简单格式：仅指定容器端口
      - "5138"

      # 标准格式：<域名>:<容器端口>/<协议>
      - "mes.bosch-esp.com:5138/https"

      # 多域名：同一服务的多个入口
      - "mes.bosch-esp.com:5138/https"
      - "mes.internal.bosch.com:5138/http"
```

**`x-ports` 格式字段：**

| 部分 | 必填 | 说明 | 示例 |
|------|------|------|------|
| `域名` | 否 | 自定义域名（省略则使用 `*.uncld.dev`） | `mes.bosch-esp.com` |
| `:` | 是 | 分隔符 | `:` |
| `容器端口` | 是 | 服务内部监听的端口 | `5138` |
| `/` | 是 | 分隔符 | `/` |
| `协议` | 是 | `https`（推荐）或 `http` | `https` |

#### 7.3.5 `x-ports` 与标准 `ports` 的关系

| 特性 | `ports`（标准） | `x-ports`（Uncloud 扩展）|
|------|----------------|--------------------------|
| 作用 | 将容器端口映射到宿主机 | 在 Caddy 中创建反向代理规则 |
| 是否必需 | 是（服务间通信需要） | 否（仅对外暴露需要） |
| 是否影响 mesh 内通信 | 否 | 否 |
| HTTPS | 否 | 是（自动 Let's Encrypt） |
| 域名绑定 | 否 | 是 |

> **注意：** `ports` 和 `x-ports` 可以独立使用。`ports` 控制 Docker 级别的端口映射，`x-ports` 控制 Caddy 反向代理。对于对外暴露的服务，两者通常都需要。

---

### 7.4 自定义域名

生产环境建议使用自有域名（如 `mes.bosch-esp.com`）而非 `*.uncld.dev`。

#### 7.4.1 方法 A：在 `compose.yaml` 中配置（推荐）

直接编辑 `docker/compose.yaml`，为 `mes-web` 添加 `x-ports`：

```yaml
services:
  mes-web:
    ports:
      - "80:5138"
    x-ports:
      - "mes.bosch-esp.com:5138/https"
```

重新部署：

```bash
uc deploy -f docker/compose.yaml -e .env.production
```

> 部署后 Caddy 会自动为 `mes.bosch-esp.com` 申请 Let's Encrypt 证书。

#### 7.4.2 方法 B：通过 `uc` CLI 动态设置

不修改 `compose.yaml`，直接通过 CLI 更新：

```bash
# 更新服务的对外域名
uc service update mes-web --domain mes.bosch-esp.com

# 或者指定完整 x-ports
uc service update mes-web \
  --x-ports "mes.bosch-esp.com:5138/https"

# 重新部署生效
uc deploy -f docker/compose.yaml -e .env.production
```

#### 7.4.3 DNS 配置

在域名管理控制台添加 DNS 记录：

| 记录类型 | 主机名 | 值 | 说明 |
|---------|--------|-----|------|
| **CNAME** | `mes` | `<集群ID>-<机器名>.uncld.dev` | 推荐：跟随 Uncloud DNS 变化 |
| **A** | `mes` | `<服务器公网IP>` | 备用：直接指向服务器 IP |

**CNAME 示例：**

```
mes.bosch-esp.com.  CNAME  automes-abc123-server-1.uncld.dev.
```

**A 记录示例：**

```
mes.bosch-esp.com.  A  203.0.113.10
```

#### 7.4.4 验证自定义域名

```bash
# 等待 DNS 生效（通常 1-10 分钟）
# 验证域名解析
nslookup mes.bosch-esp.com
# 应指向 uncld.dev 或您的服务器 IP

# 验证 HTTPS
dig mes.bosch-esp.com +short
curl -f https://mes.bosch-esp.com/health
# 预期：返回健康状态

# 查看证书
curl -vI https://mes.bosch-esp.com/ 2>&1 | grep -i "server certificate"
# 预期：Let's Encrypt 证书
```

---

### 7.5 Caddy HTTPS 行为

#### 7.5.1 自动 TLS

Caddy 自动处理以下 HTTPS 事宜：

| 功能 | 说明 |
|------|------|
| 证书申请 | 自动通过 Let's Encrypt 申请 TLS 证书 |
| 证书续期 | 自动在到期前 30 天续期 |
| HTTP 重定向 | 自动将 HTTP 80 重定向到 HTTPS 443 |
| HSTS | 可选配置 HTTP Strict-Transport-Security |
| OCSP Stapling | 默认启用，提高 TLS 握手性能 |

#### 7.5.2 证书存储

证书由 Caddy 在容器内部自动管理。如需查看或备份：

```bash
# 查看 Caddy 证书信息
uc exec caddy -- caddy cert-info

# 证书存储路径（Caddy 容器内）
# /data/caddy/pki/authorities/local/
# /data/caddy/certificates/
```

#### 7.5.3 HTTPS 强制重定向

Uncloud 自动配置 HTTP→HTTPS 重定向。访问 `http://mes.bosch-esp.com` 会自动 301 跳转到 `https://mes.bosch-esp.com`。

#### 7.5.4 Let's Encrypt 注意事项

- 服务器 **必须** 有公网 IP，且 80/443 端口可达，Let's Encrypt 才能验证域名所有权
- 如果服务器在 NAT 后面，需要确保端口转发正确
- 如果使用 `*.uncld.dev` 域名，Uncloud DNS 自动处理验证
- 首次证书申请可能需要 10-30 秒

---

### 7.6 多域名配置

#### 7.6.1 同一服务多个域名

```yaml
services:
  mes-web:
    ports:
      - "80:5138"
    x-ports:
      # 外部用户
      - "mes.bosch-esp.com:5138/https"
      # 内部用户（厂区内网）
      - "mes.internal.bosch.com:5138/https"
      # 备用域名（通过默认 uncld.dev）
      - "5138"
```

部署后，三个 URL 均可访问同一个 Web 服务：

```
https://mes.bosch-esp.com          # 外部用户
https://mes.internal.bosch.com      # 内部用户
https://automes-abc.uncld.dev       # 备用访问
```

#### 7.6.2 多服务多域名

如果后续扩展出运维后台、API 文档等独立服务：

```yaml
services:
  mes-web:
    ports:
      - "80:5138"
    x-ports:
      - "mes.bosch-esp.com:5138/https"  # 主系统

  mes-admin:
    ports:
      - "80:5200"
    x-ports:
      - "admin.bosch-esp.com:5200/https"  # 管理后台
```

---

### 7.7 多机环境下的流量路由

#### 7.7.1 单机 → 多机拓扑变化

当服务分布在多台机器上时，流量路由方式发生了变化：

| 架构 | 流量路径 |
|------|---------|
| **单机** | 浏览器 → Caddy（同一台）→ mes-web（同一台）| 
| **双机**（DB 独占） | 浏览器 → Caddy（app-server）→ mes-web（app-server）→ mes-api（app-server）←WireGuard→ postgres（db-1）|
| **三机** | 浏览器 → Caddy（web-1）→ mes-web（web-1）←WireGuard→ mes-api（api-1）←WireGuard→ postgres（db-1）|

#### 7.7.2 Caddy 在每台机器上

Uncloud 在**每台集群机器上**都部署了一个 Caddy 全局服务实例。这意味着：

- Caddy 始终与 `mes-web` 在**同一台机器**上运行
- 跨机通信通过 WireGuard mesh（低延迟 + 加密）
- 任何机器的 `80`/`443` 端口都可以接收外部流量

#### 7.7.3 负载均衡

如果 `mes-web` 分布在多台机器上（`scale > 1`），Caddy 会自动在它们之间进行负载均衡：

```yaml
services:
  mes-web:
    x-machines:
      - web-1
      - web-2
    scale: 2
    ports:
      - "80:5138"
    x-ports:
      - "mes.bosch-esp.com:5138/https"
```

> **注意：** Blazor Server 使用 SignalR 长连接，需要 **sticky sessions**（会话亲和性）。多实例 Web 需要额外配置 SignalR Redis 背板。生产建议单实例 Web + 多实例 API。

#### 7.7.4 DNS 轮询

如果使用 A 记录指向多台服务器的公网 IP，可以实现简单的 DNS 轮询负载均衡：

```
mes.bosch-esp.com.  A  203.0.113.10   # web-1
mes.bosch-esp.com.  A  203.0.113.11   # web-2
```

---

### 7.8 安全加固

#### 7.8.1 最小暴露原则

| 服务 | 是否对外暴露 | 原因 |
|------|-------------|------|
| `mes-web` | ✅ 是（端口 80/443） | 浏览器访问 Web UI |
| `mes-api` | ❌ **否** | REST API 仅 Web 后端调用 |
| `postgres` | ❌ **否** | 数据库仅 API 调用 |
| `pg-backup` | ❌ **否** | 内部备份服务 |

#### 7.8.2 端口绑定策略

```yaml
services:
  mes-api:
    ports:
      # 127.0.0.1 = 仅宿主机本机可访问
      - "127.0.0.1:5040:5040"

  postgres:
    ports:
      # 不对外暴露，仅 Docker 内部网络 + WireGuard mesh
      - "127.0.0.1:5432:5432"
```

#### 7.8.3 IP 白名单（可选）

如需更严格的访问控制，可以在 Caddy 层面限制来源 IP：

Uncloud 目前不支持直接在 `x-ports` 中设置 IP 白名单。如需此功能，建议：

1. 在 Caddy 配置中加入 IP 过滤（需自定义 Caddyfile）
2. 或在网络层面使用防火墙规则：

```bash
# 仅允许厂区 VPN IP 段访问 Web
sudo iptables -A INPUT -p tcp --dport 80 -s 10.0.0.0/8 -j ACCEPT
sudo iptables -A INPUT -p tcp --dport 443 -s 10.0.0.0/8 -j ACCEPT
sudo iptables -A INPUT -p tcp --dport 80 -j DROP
sudo iptables -A INPUT -p tcp --dport 443 -j DROP
```

#### 7.8.4 安全总结

| 措施 | 状态 | 说明 |
|------|------|------|
| HTTPS | ✅ 自动 | Caddy + Let's Encrypt |
| HTTP→HTTPS 重定向 | ✅ 自动 | Uncloud 默认配置 |
| API 不对外暴露 | ✅ 已配置 | `127.0.0.1` 绑定 |
| 数据库不对外暴露 | ✅ 已配置 | `127.0.0.1` 绑定 |
| 跨机加密通信 | ✅ WireGuard | 自动密钥管理 |
| IP 白名单 | ⚠️ 可选 | 需防火墙规则 |
| WAF | ❌ 暂不支持 | 需额外部署 |

---

### 7.9 流量监控与排障

#### 7.9.1 查看 Caddy 日志

```bash
# 查看 Caddy 访问日志
uc logs caddy | grep mes.bosch-esp.com

# 实时监控 HTTP 请求
uc logs -f caddy | grep --line-buffered "mes-web"
```

#### 7.9.2 测试 HTTPS 证书

```bash
# 查看证书详情
openssl s_client -connect mes.bosch-esp.com:443 -servername mes.bosch-esp.com 2>/dev/null | openssl x509 -noout -text | grep -E "Subject:|Not Before:|Not After:"

# 检查证书链
curl -vI https://mes.bosch-esp.com/ 2>&1 | grep -i "certificate"
```

#### 7.9.3 检查 Caddy 配置

```bash
# 查看 Caddy 已配置的路由
uc exec caddy -- caddy api routes

# 查看 Caddy 配置 JSON
uc exec caddy -- caddy adapt
```

#### 7.9.4 流量统计

```bash
# 查看 Caddy 流量统计
uc exec caddy -- cat /data/caddy/metrics 2>/dev/null | grep -E "(http_request|tls)" | head -20
```

---

### 7.10 常见问题

#### 域名解析失败

```
curl: (6) Could not resolve host: mes.bosch-esp.com
```

**原因：** DNS 记录未生效或配置错误。

**解决：**
```bash
# 检查 DNS 解析
dig mes.bosch-esp.com +trace
# 确认 CNAME 或 A 记录已存在
```

#### HTTPS 证书申请失败

```
Caddy: failed to obtain certificate: acme: error: 403
```

**原因：** Let's Encrypt 无法验证域名所有权（80/443 端口不可达）。

**解决：**
```bash
# 确认防火墙已放行 80 和 443
sudo ufw status verbose

# 检查端口可达性（从外网）
nc -zv <公网IP> 80
nc -zv <公网IP> 443
```

#### 只能 HTTP 不能 HTTPS

**原因：** 可能是 `x-ports` 协议设置为了 `http` 而非 `https`。

**解决：**
```yaml
# 错误：
x-ports:
  - "mes.bosch-esp.com:5138/http"   # 使用 http 协议

# 正确：
x-ports:
  - "mes.bosch-esp.com:5138/https"  # 使用 https 协议，Caddy 自动 TLS
```

#### 端口冲突

```
Error: port 80 is already in use
```

**原因：** 宿主机上已有其他 Web 服务器（Nginx、Apache）占用 80/443。

**解决：**
```bash
# 停用冲突服务
sudo systemctl stop nginx
sudo systemctl disable nginx

# 或更改 Caddy 端口（不推荐）
# 修改 compose.yaml 中的 ports
```

#### Caddy 未生成 URL

```bash
uc inspect mes-web
# 不显示 URL
```

**原因：** `x-ports` 未配置，或部署时未包含暴露配置。

**解决：**
```bash
# 检查 compose.yaml 中是否有 x-ports
# 或手动添加
uc service update mes-web --x-ports "80"
```

#### 厂区内网无公网 IP

如果厂区服务器没有公网 IP，但需要通过互联网访问 MES：

1. **使用 Tailscale/WireGuard VPN**（推荐，见 TD.8）
   - 在服务器上安装 Tailscale
   - 运维人员通过 Tailscale 客户端接入
   - 通过 Tailscale 分配的 `100.x.x.x` IP 访问

2. **使用 frp / ngrok**（临时方案）
   - 在有公网 IP 的中转服务器上部署 frp server
   - MES 服务器通过 frp client 建立隧道

3. **使用 Uncloud 自带 `*.uncld.dev`**
   - Uncloud DNS 需要公网 IP 才能工作
   - 如无公网 IP，`*.uncld.dev` 无法生效

---

## 8. 零停机滚动部署

> 本节对应 **TD.7 零停机滚动部署验证**。

### 8.1 零停机原理

Uncloud 的 `uc deploy` 在服务有多副本时自动执行零停机滚动更新：

```
时间 →
┌───────────┐  ┌───────────┐  ┌───────────┐  ┌───────────┐
│  旧 v1    │  │  旧 v1    │  │  旧 v1    │  │  新 v2    │
│  api-1    │  │  api-1    │  │  停止     │  │  api-1    │
│  提供服务  │  │  提供服务  │  │           │  │  提供服务  │
└───────────┘  └───────────┘  └───────────┘  └───────────┘
┌───────────┐  ┌───────────┐  ┌───────────┐  ┌───────────┐
│  旧 v1    │  │  新 v2    │  │  新 v2    │  │  新 v2    │
│  api-2    │→ │  api-2    │→ │  api-2    │→ │  api-2    │
│  提供服务  │  │  启动中    │  │  提供服务  │  │  提供服务  │
└───────────┘  └───────────┘  └───────────┘  └───────────┘
     t=0           t=5s           t=10s          t=15s
    部署开始      新容器启动      切换流量      旧容器停止
```

**关键条件：**

| 条件 | 说明 | 当前 AutoMES 状态 |
|------|------|-------------------|
| 至少 2 副本 | `scale ≥ 2` 分布在 ≥ 2 台机器 | ❌ 单实例（需扩展）|
| 健康检查 | 新容器健康检查通过后才切换流量 | ✅ 已配置 |
| 共享存储 | 多副本需共享状态（如 Session） | ⚠️ SignalR Redis 背板 |
| Sticky Session | Blazor Server SignalR 长连接 | ⚠️ 需额外配置 |

> **当前限制：** AutoMES 生产部署为单实例配置，`uc deploy` 更新时会有短暂停机（几秒）。要实现真正的零停机，需要扩展 API 到多副本 + 配置 SignalR Redis 背板。

### 8.2 多副本配置

要实现零停机滚动部署，需要将 `mes-api` 和 `mes-web` 配置为多副本：

```yaml
services:
  # ── 可零停机滚动更新的 API ──
  mes-api:
    build:
      context: ..
      dockerfile: src/MesAdmin.Api/Dockerfile
    x-machines:
      - api-1
      - api-2
    scale: 2
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:5040/health"]
      interval: 10s     # 缩短检查间隔
      timeout: 5s
      start_period: 15s  # 给新容器足够启动时间
      retries: 3
    deploy:
      update_config:
        order: start-first   # 先启动新容器，再停止旧容器
        parallelism: 1        # 一次更新一个副本
        delay: 10s            # 副本间延迟

  # ── 单副本 Web（零停机需 SignalR Redis 背板）──
  mes-web:
    build:
      context: ..
      dockerfile: src/MesAdmin.Web/Dockerfile
    x-machines: web-1
    scale: 1
    # Blazor Server 零停机需要 sticky sessions
    # 当前暂为单实例
```

### 8.3 滚动更新执行

```bash
# 1. 提交代码变更
# 2. 确保工作目录干净
git status

# 3. 部署（自动滚动更新）
uc deploy -f docker/compose.yaml -e .env.production
```

**部署计划输出（多副本）：**

```
▶ Planning deployment...
  Service: mes-api
    Image:      automes/mes-api:20260707.1234.abc1234 → 20260708.0900.def5678
    Machine:    [api-1, api-2] (×2 replicas)
    Strategy:   rolling update (start-first, parallel: 1, delay: 10s)
    
  Service: mes-web
    Image:      automes/mes-web:20260707.1234.abc1234 → 20260708.0900.def5678
    Machine:    web-1 (×1 replica)
    Strategy:   recreate (service will restart)
```

### 8.4 滚动部署验证脚本

以下脚本验证零停机部署的可用性：

```bash
#!/bin/bash
# validate-rolling-deploy.sh
# 在部署期间持续发送 HTTP 请求，验证零停机
# 用法: ./validate-rolling-deploy.sh <域名>

set -euo pipefail

DOMAIN="${1:?用法: $0 <域名>}"
DURATION="${2:-120}"  # 监控时长（秒）
INTERVAL=1             # 请求间隔

echo "=== 开始零停机验证: ${DOMAIN} ==="
echo "监控时长: ${DURATION}s, 请求间隔: ${INTERVAL}s"

FAILURES=0
REQUESTS=0
START_TIME=$(date +%s)
END_TIME=$((START_TIME + DURATION))

while [ $(date +%s) -lt $END_TIME ]; do
  HTTP_CODE=$(curl -sf -o /dev/null -w "%{http_code}" "https://${DOMAIN}/health" 2>/dev/null || echo "000")
  REQUESTS=$((REQUESTS + 1))
  
  if [ "$HTTP_CODE" = "000" ]; then
    FAILURES=$((FAILURES + 1))
    echo "❌ $(date +%H:%M:%S) 请求失败 (超时/连接拒绝)"
  elif [ "$HTTP_CODE" != "200" ]; then
    FAILURES=$((FAILURES + 1))
    echo "⚠️  $(date +%H:%M:%S) HTTP ${HTTP_CODE}"
  else
    echo "✅ $(date +%H:%M:%S) HTTP 200"
  fi
  
  sleep $INTERVAL
done

echo ""
echo "=== 验证结果 ==="
echo "总请求: ${REQUESTS}"
echo "失败:    ${FAILURES}"
echo "可用性:  $(echo "scale=2; (${REQUESTS} - ${FAILURES}) * 100 / ${REQUESTS}" | bc)%"

if [ "$FAILURES" -eq 0 ]; then
  echo "✅ 零停机验证通过!"
else
  echo "❌ 零停机验证失败: ${FAILURES} 次请求失败"
  exit 1
fi
```

### 8.5 滚动部署执行流程

```bash
# 终端 1：启动验证脚本
./validate-rolling-deploy.sh mes.bosch-esp.com 180

# 终端 2：执行滚动部署
uc deploy -f docker/compose.yaml -e .env.production

# 观察终端 1 的输出，确认零停机
```

**预期结果：**

- 多副本 API（`scale: 2`）：**无中断**，可用性 100%
- 单副本 Web（`scale: 1`）：**短暂中断**（1-5 秒），可用性 > 99%

### 8.6 滚动部署后的验证

```bash
# 1. 确认所有副本已更新
uc ls
# mes-api  | Running  | replicas: 2/2  | api-1:20260708, api-2:20260708

# 2. 检查每个副本的健康状态
uc inspect mes-api

# 3. 确认日志无异常
uc logs mes-api --since 5m | grep -i error | tail -5

# 4. 执行功能验证
curl -f https://mes.bosch-esp.com/health
```

### 8.7 部署策略对比

| 策略 | 适用场景 | 停机时间 | 配置 |
|------|---------|---------|------|
| **滚动更新**（默认） | 多副本服务 | 零停机 | `scale ≥ 2` + 健康检查 |
| **重建**（单副本） | 单副本服务 | 5-30 秒 | `scale = 1`（默认行为）|
| **先启动后停止** | 有状态服务 | 零停机 | `deploy.update_config.order: start-first` |
| **先停止后启动** | 资源受限环境 | 停机 | `deploy.update_config.order: stop-first` |

### 8.8 当前 AutoMES 零停机能力

| 服务 | 当前配置 | 零停机能力 | 改进方向 |
|------|---------|-----------|---------|
| `postgres` | 单实例 | ❌ 不支持 | PostgreSQL HA（流复制 + Patroni）|
| `mes-api` | 单实例 | ❌ 短暂中断 | 配置 `scale: 2` + `x-machines` 双机 |
| `mes-web` | 单实例 | ⚠️ SignalR 限制 | 配置 SignalR Redis 背板 + `scale: 2` |
| `pg-backup` | 单实例 | ✅ 可中断 | 无需零停机 |

---

## 9. 运维命令速查

### 本地 CLI

| 命令 | 说明 |
|------|------|
| `uc version` | 查看版本 |
| `uc machine ls` | 列出所有集群机器 |
| `uc machine inspect <name>` | 查看机器详情（资源、网格 IP）|
| `uc machine rm <name>` | 从集群中移除机器 |
| `uc ls` | 列出所有部署的服务 |
| `uc inspect <service>` | 查看服务详情 |
| `uc ps` | 列出运行中的容器 |
| `uc logs <service>` | 查看服务日志 |
| `uc logs -f` | 实时日志流 |
| `uc deploy -f compose.yaml` | 部署/更新服务 |
| `uc rm <service>` | 移除服务 |
| `uc wg show` | 查看 WireGuard 网格状态 |
| `uc wg show --machine <name>` | 查看指定机器的网格详情 |

### 服务器端（SSH）

| 命令 | 说明 |
|------|------|
| `uc service ls` | 列出服务器上的 Uncloud 服务 |
| `uc service inspect <name>` | 查看服务详情 |
| `docker ps` | 查看所有容器 |
| `journalctl -u uncloudd -f` | 查看 uncloudd 日志 |
| `systemctl status uncloudd` | 查看守护进程状态 |
| `cat /etc/uncloud/config.yaml` | 查看 Uncloud 本地配置 |

---

## 10. 故障排查

### 9.1 SSH 连接失败

```
Error: ssh: handshake failed
```

**原因：** SSH 密钥未配置或权限不足。

**解决：**
```bash
# 确保公钥已复制到服务器
ssh-copy-id root@<服务器IP>

# 或使用密码认证（需要服务器端配置 PasswordAuthentication yes）
uc machine init root@<服务器IP> --password
```

### 9.2 Docker 安装失败

```
Error: failed to install docker
```

**原因：** 服务器系统版本不支持。

**解决：** 手动安装 Docker，然后重新执行 `uc machine init`：
```bash
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER
```

### 9.3 端口被占用

```
Error: port 80 is already in use
```

**原因：** 服务器上已有其他程序占用 `80`/`443` 端口（如 Nginx、Apache）。

**解决：**
```bash
# 查看端口占用
ss -tlnp | grep -E ':(80|443)\s'

# 停用冲突服务
systemctl stop nginx
systemctl disable nginx
```

### 9.4 镜像构建失败

```
Building mes-api... failed
```

**原因：** Docker 构建上下文或网络问题。

**解决：**
```bash
# 在本地构建验证
docker compose -f docker/compose.yaml build mes-api

# 推送本地镜像到服务器
uc image push mes-api:latest

# 或使用外部镜像仓库
docker tag mes-api:latest registry.example.com/mes-api:latest
docker push registry.example.com/mes-api:latest
```

### 9.5 WireGuard 连接超时

```
Error: mesh connection timeout
```

**原因：** UDP 端口 `51820` 未放行，或 NAT 穿透失败。

**解决：**
```bash
# 服务器端检查防火墙
ufw status
# 确保 51820/udp 已放行

# 或使用 iptables
iptables -A INPUT -p udp --dport 51820 -j ACCEPT
```

### 9.6 健康检查失败

```
mes-api: unhealthy (1/3 retries)
```

**原因：** API 尚未完成 EF Core Migration，或数据库连接失败。

**排查：**
```bash
# 查看 API 日志
uc logs mes-api

# 检查数据库连接
uc exec postgres -- pg_isready -U mes -d automes

# 手动运行 Migration（如有必要）
uc exec mes-api -- dotnet ef database update
```

---

## 附录 A：单服务器快速部署脚本

以下脚本一键完成 TD.2-TD.5 全部步骤（针对单服务器场景）：

```bash
#!/bin/bash
# ── AutoMES Uncloud 一键部署脚本 ──
# 用法: ./deploy.sh <服务器IP> <POSTGRES_PASSWORD> <JWT_SECRET>

set -euo pipefail

SERVER_IP="${1:?用法: $0 <服务器IP> <POSTGRES_PASSWORD> <JWT_SECRET>}"
POSTGRES_PASSWORD="${2:?}"
JWT_SECRET="${3:?}"
DOMAIN="${4:-}"  # 可选自定义域名

echo "=== 1/5: 安装 uc CLI ==="
if ! command -v uc &> /dev/null; then
  curl -fsS https://get.uncloud.run/install.sh | sh
fi

echo "=== 2/5: 初始化服务器 ==="
uc machine init "root@${SERVER_IP}"

echo "=== 3/5: 创建 .env.production ==="
cat > .env.production << EOF
POSTGRES_PASSWORD=${POSTGRES_PASSWORD}
JWT_SECRET=${JWT_SECRET}
SAP_USE_REAL_CLIENT=false
SMTP_ENABLED=false
EOF

echo "=== 4/5: 部署 MES 服务 ==="
uc deploy -f docker/compose.yaml -e .env.production

echo "=== 5/5: 验证部署 ==="
sleep 10
curl -f "https://automes-$(uc machine ls --format '{{.Name}}' | head -1).uncld.dev/health"
echo ""
echo "=== ✅ 部署完成 ==="
echo "Web UI: https://automes-$(uc machine ls --format '{{.Name}}' | head -1).uncld.dev"
```

---

## 附录 B：相关文档

| 文档 | 路径 |
|------|------|
| 生产 compose.yaml | `docker/compose.yaml` |
| 环境变量模板 | `docker/.env.production.example` |
| API Dockerfile | `src/MesAdmin.Api/Dockerfile` |
| Web Dockerfile | `src/MesAdmin.Web/Dockerfile` |
| 部署任务清单 | `TASKS.md`（TD.1-TD.8）|
| 可观测性栈 | `docker/observability/compose.yaml` |
| Uncloud 官方文档 | https://uncloud.run/docs/ |
