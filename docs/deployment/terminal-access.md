# 终端 VPN 接入指南

> **对应任务：** TD.8 — 终端 VPN 接入补充
> **文档版本：** v1.0 — 2026-07-07
> **适用范围：** 车间工位终端（Avalonia/Windows/Linux）、运维人员远程接入

---

## 1. 背景：为什么需要终端 VPN

### 1.1 网络隔离策略

AutoMES 生产部署遵循**最小暴露原则**：

| 服务 | 是否对外暴露 | 访问方式 |
|------|-------------|---------|
| `mes-web` (Blazor UI) | ✅ HTTPS 80/443 | 浏览器通过 Caddy + Let's Encrypt |
| `mes-api` (REST API) | ❌ 127.0.0.1 绑定 | 仅 Docker 内部网络 + WireGuard mesh |
| `postgres` (数据库) | ❌ 127.0.0.1 绑定 | 仅 Docker 内部网络 + WireGuard mesh |
| **工位终端** | ❌ 不直接暴露 | **需通过 VPN 接入厂区网络** |

### 1.2 Uncloud 与终端 VPN 的分工

```
                    ┌──────────────────────────────────────┐
                    │          Uncloud WireGuard Mesh       │
                    │  （服务器间组网，自动管理）              │
                    │                                      │
                    │   db-1 ◄────► api-1 ◄────► web-1     │
                    │   10.100.0.1   10.100.0.2  10.100.0.3 │
                    └──────────┬───────────────────────────┘
                               │
                    ═══════════╪═══════════════════════
                    （厂区物理网络 / 防火墙）
                               │
          ┌────────────────────┼────────────────────┐
          │                    │                     │
    ┌─────┴─────┐      ┌──────┴──────┐      ┌──────┴──────┐
    │ 产线1 工位  │      │ 产线2 工位   │      │ 运维笔记本电脑 │
    │ 终端       │      │ 终端        │      │ (远程)       │
    │ Win/Linux  │      │ Win/Linux   │      │              │
    └────────────┘      └─────────────┘      └─────────────┘
         ▲                     ▲                     ▲
         └──────────┬──────────┘                     │
                    │                                 │
              ┌─────┴─────┐                    ┌──────┴──────┐
              │  Tailscale │                    │  Tailscale   │
              │  客户端    │                    │  客户端      │
              └───────────┘                    └─────────────┘
```

**核心区别：**

| 组件 | Uncloud WireGuard Mesh | 终端 VPN (Tailscale/WireGuard) |
|------|-----------------------|--------------------------------|
| 用途 | **服务器间**组网 | **终端→服务器**接入 |
| 管理 | `uc CLI` 自动管理 | 需独立部署和管理 |
| 节点 | 集群内的 Linux 服务器 | 工位终端 + 运维人员设备 |
| 客户端 | 无（Docker 自动加入 mesh） | 需安装客户端软件 |
| 防火墙 | 需要放行 51820/udp | 无需放行端口（出站连接） |

> **一句话：** Uncloud 解决「服务器怎么互相找到」，终端 VPN 解决「人/终端怎么找到服务器」。

---

## 2. 方案对比与选择

### 2.1 方案总览

| 方案 | 部署难度 | 维护成本 | 适用场景 | 成本 |
|------|---------|---------|---------|------|
| **Tailscale**（推荐） | ⭐ 低 | 几乎为零 | 任何规模，特别是远程运维+车间终端混合 | $8-15/用户/月 |
| **Headscale**（自托管） | ⭐⭐⭐ 中高 | 高（需自维护服务器） | 数据主权要求极高，禁止任何外部通信 | 免费（需基础设施）|
| **纯 WireGuard** | ⭐⭐⭐⭐ 高 | 高 | 简单站点直连，少量终端 | 免费 |
| **OpenVPN** | ⭐⭐⭐⭐ 高 | 高 | 遗留设备兼容 | 免费 |

### 2.2 详细对比

| 特性 | Tailscale ✅ | Headscale ⚠️ | 纯 WireGuard ❌ | OpenVPN ❌ |
|------|-------------|--------------|----------------|------------|
| NAT 穿透 | ✅ 自动（DERP relay） | ✅ 自动（需自建 DERP） | ❌ 需公网 IP + 端口转发 | ❌ 需端口转发 |
| 集中管理 | ✅ Web 控制台 | ✅ 自建控制台 | ❌ 无 | ❌ 无 |
| ACL 访问控制 | ✅ 细粒度 | ✅ 细粒度 | ❌ iptables 手动 | ❌ 手动 |
| 多平台客户端 | ✅ Win/Mac/Linux/iOS/Android | ✅ 同 Tailscale | ✅ 内核内置 | ✅ 通用 |
| 设备数限制 | ✅ 免费 3 用户，付费 20+ | ✅ 无限制 | ✅ 无限制 | ✅ 无限制 |
| 自动 Mesh | ✅ 完全自动 | ✅ 完全自动 | ❌ 全手动配置 | ❌ 客户端-服务器 |
| 审计日志 | ✅ 商业版 | ❌ 无 | ❌ 无 | ❌ 无 |
| SSO 集成 | ✅ OIDC/OAuth | ✅ OIDC | ❌ 无 | ❌ 插件 |

### 2.3 推荐决策树

```
需要多少终端接入？
├── < 3 个终端 + 3 个运维人员
│   └── Tailscale Free（3 用户免费）
│       └── 注意：商业环境需购买商业许可
│
├── 3-50 个终端 + 5-20 个运维人员
│   ├── Tailscale Standard（推荐，~$8/用户/月）
│   └── 理由：零维护、集中管理、ACL 细粒度控制
│
├── > 50 个终端，且数据主权要求高
│   ├── Headscale（自托管，推荐）
│   └── 理由：不限设备数，数据完全自控，批量部署
│
└── 仅少量固定终端（< 5）且服务器有公网 IP
    └── 纯 WireGuard
    └── 理由：最简单，无需依赖第三方
```

---

## 3. 方案 A：Tailscale（推荐）

### 3.1 架构设计

```
┌──────── Tailscale Tailnet (100.x.x.x/10) ──────────────────┐
│                                                             │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐  │
│  │ mes-server   │    │ 产线1 终端    │    │ 产线2 终端    │  │
│  │ (Subnet Router) │  │ 100.x.x.2   │    │ 100.x.x.3   │  │
│  │ 100.x.x.1    │    │              │    │              │  │
│  └──────┬───────┘    └──────────────┘    └──────────────┘  │
│         │                                                    │
│         │ (路由 10.100.0.0/24 到 mes-server)                 │
│         ▼                                                    │
│  ┌──────────────┐                                           │
│  │ 运维笔记本    │    ┌──────────────┐                      │
│  │ 100.x.x.10   │    │ 质量工程师    │                      │
│  │              │    │ 100.x.x.11   │                      │
│  └──────────────┘    └──────────────┘                      │
│                                                             │
│  终端通过 Tailscale IP (100.x.x.x) 直接访问：               │
│  - mes-web:  http://100.x.x.1:5138 或 https://mes.bosch.com │
│  - mes-api:  http://100.x.x.1:5040                          │
└─────────────────────────────────────────────────────────────┘
```

### 3.2 安装 Tailscale

#### 3.2.1 MES 服务器（Subnet Router 模式）

Tailscale 在 MES 服务器上以 **Subnet Router（子网路由）** 模式运行，使其能够将 Uncloud WireGuard mesh 网段（`10.100.0.0/24`）暴露给 Tailscale 网络。

```bash
# 1. SSH 到 MES 服务器（任一集群节点）
ssh root@<服务器IP>

# 2. 安装 Tailscale
curl -fsSL https://tailscale.com/install.sh | sh

# 3. 启动并认证（浏览器打开 URL 登录）
tailscale up --advertise-routes=10.100.0.0/24 --accept-routes --hostname=mes-server

# 4. 认证完成后，在 Tailscale 控制台启用子网路由
#    → Machines → mes-server → Edit route settings → Enable 10.100.0.0/24
```

**验证子网路由：**

```bash
# 从 Tailscale 管理控制台或本地终端测试
tailscale status
# 预期输出：
# 100.x.x.1    mes-server          ....    linux   -
# 10.100.0.0/24 (advertised routes)

# 从 MES 服务器测试路由
tailscale netcheck
```

> **为什么需要 Subnet Router？** MES 部署在 Uncloud WireGuard mesh（`10.100.0.0/24`）中，各服务通过 Docker 内部 DNS 解析。Subnet Router 让 Tailscale 客户端可以直接访问 `10.100.0.x` 网段，无需在每台机器上安装 Tailscale。

#### 3.2.2 工位终端安装

**Windows 工位终端：**

```powershell
# 1. 下载并安装 Tailscale
#    浏览器访问 https://tailscale.com/download/windows
#    或使用 winget:
winget install Tailscale.Tailscale

# 2. 启动 Tailscale
Start-Process "C:\Program Files\Tailscale\Tailscale.exe"

# 3. 认证（浏览器自动打开）
#    登录 Tailscale 管理控制台

# 4. 验证连接
tailscale status
# 预期：看到 mes-server 处于 Connected 状态
```

**Linux 工位终端（Ubuntu/Debian）：**

```bash
# 1. 安装
curl -fsSL https://tailscale.com/install.sh | sh

# 2. 启动并认证
sudo tailscale up --accept-routes --hostname=line1-terminal

# 3. 验证
tailscale status
```

**macOS 运维笔记本：**

```bash
# 1. 安装
brew install --cask tailscale

# 2. 或从 https://tailscale.com/download/mac 下载安装包

# 3. 启动并使用公司 SSO 登录
```

### 3.3 访问 MES 服务

终端接入 Tailscale 后，通过以下方式访问 MES：

```bash
# 方式 1：通过 Subnet Router 直接访问 Uncloud Mesh IP
curl http://10.100.0.3:5138/           # mes-web
curl http://10.100.0.2:5040/health     # mes-api

# 方式 2：通过 Tailscale MagicDNS（如有启用）
curl http://mes-server:5138/

# 方式 3：对外 HTTPS（通过公网 Caddy）
curl https://mes.bosch-esp.com/health
```

**推荐配置：** 工位终端使用**对内 HTTP**（方式 1/2），运维人员使用**对外 HTTPS**（方式 3）。

### 3.4 ACL 安全策略

Tailscale 的 ACL（访问控制列表）是生产部署的核心安全能力。

以下是为 AutoMES 设计的 ACL 策略示例，在 [Tailscale 控制台](https://login.tailscale.com/admin/acls) 中配置：

```json
{
  // AutoMES Tailscale ACL 策略
  // 应用于：Tailnet 中的所有节点
  // 最后更新：2026-07-07

  "acls": [
    // ── 规则 1：MES 服务器可以被所有终端访问 ──
    {
      "action": "accept",
      "src":    ["tag:operator", "tag:engineer", "tag:admin"],
      "dst":    ["tag:mes-server:*"]
    },

    // ── 规则 2：产线终端只能访问 MES 服务 ──
    {
      "action": "accept",
      "src":    ["tag:operator"],
      "dst":    [
        "tag:mes-server:80",     // Web UI
        "tag:mes-server:443",    // HTTPS
        "tag:mes-server:5040"    // REST API (可选)
      ]
    },

    // ── 规则 3：运维人员可以 SSH 到服务器 ──
    {
      "action": "accept",
      "src":    ["tag:engineer", "tag:admin"],
      "dst":    ["tag:mes-server:22"]
    },

    // ── 规则 4：管理员完全访问 ──
    {
      "action": "accept",
      "src":    ["tag:admin"],
      "dst":    ["*:*"]
    },

    // ── 规则 5：阻止终端间互相访问（隔离） ──
    {
      "action": "accept",
      "src":    ["tag:operator"],
      "dst":    ["tag:operator:*"],
      "proto":  "icmp"  // 仅允许 ping，禁止其他流量
    }
  ],

  // ── 节点标签 ──
  "tagOwners": {
    "tag:mes-server":  ["autogroup:admin"],
    "tag:operator":    ["autogroup:admin"],
    "tag:engineer":    ["autogroup:admin"],
    "tag:admin":       ["autogroup:admin"]
  },

  // ── 主机声明 ──
  "hosts": {
    "mes-server": "100.x.x.1"
  }
}
```

**在服务器上应用标签：**

```bash
# 在安装时指定标签
tailscale up --advertise-routes=10.100.0.0/24 \
  --accept-routes \
  --hostname=mes-server \
  --advertise-tags=tag:mes-server

# 注意：需要先在控制台 tagOwners 中授权
```

### 3.5 批量部署工位终端

对于大批量车间终端，可以使用 Tailscale 的无人值守（unattended）模式和预认证密钥：

#### 3.5.1 生成预认证密钥

在 [Tailscale 控制台 → Settings → Keys](https://login.tailscale.com/admin/settings/keys) 生成：

- **用途：** 产线终端批量注册
- **有效期：** 建议 30 天（一次性使用后可禁用）
- **标签：** `tag:operator`（自动打标签，跳过交互式认证）

#### 3.5.2 无人值守安装脚本

**Windows 终端（PowerShell 脚本 `deploy-tailscale.ps1`）：**

```powershell
param(
    [Parameter(Mandatory=$true)]
    [string]$AuthKey
)

# ── 1. 下载 Tailscale ──
$installer = "$env:TEMP\tailscale-installer.exe"
Invoke-WebRequest -Uri "https://pkgs.tailscale.com/stable/tailscale-setup-latest-amd64.exe" -OutFile $installer

# ── 2. 静默安装 ──
Start-Process -Wait -FilePath $installer -ArgumentList "/S"

# ── 3. 启动并认证 ──
Start-Sleep -Seconds 3
Start-Process -NoNewWindow -FilePath "C:\Program Files\Tailscale\tailscale.exe" -ArgumentList "up --authkey=$AuthKey --accept-routes --hostname=line-$(Get-Random -Minimum 100 -Maximum 999)"

# ── 4. 验证 ──
Start-Sleep -Seconds 5
$status = & "C:\Program Files\Tailscale\tailscale.exe" status
Write-Output "Tailscale status: $status"
```

**Linux 终端（bash 脚本 `deploy-tailscale.sh`）：**

```bash
#!/bin/bash
# deploy-tailscale.sh — 批量部署 Tailscale 到工位终端
# 用法: ./deploy-tailscale.sh <auth-key> [hostname]

set -euo pipefail

AUTH_KEY="${1:?用法: $0 <auth-key> [hostname]}"
HOSTNAME="${2:-line-$(hostname)-terminal}"

echo "=== 安装 Tailscale ==="
curl -fsSL https://tailscale.com/install.sh | sh

echo "=== 认证（无人值守）==="
sudo tailscale up \
  --authkey="$AUTH_KEY" \
  --accept-routes \
  --hostname="$HOSTNAME"

echo "=== 验证 ==="
tailscale status

echo "=== 完成 ==="
```

**批量部署（通过 SCCM/Ansible）：**

```yaml
# ansible/tailscale-deploy.yml
- name: 部署 Tailscale 到工位终端
  hosts: production_lines
  become: yes
  tasks:
    - name: 安装 Tailscale
      shell: curl -fsSL https://tailscale.com/install.sh | sh
      args:
        creates: /usr/bin/tailscale

    - name: 认证并加入网络
      shell: |
        tailscale up \
          --authkey="{{ tailscale_auth_key }}" \
          --accept-routes \
          --hostname="{{ inventory_hostname }}-terminal"
      register: tailscale_result

    - name: 验证连接
      shell: tailscale status
      register: status
      changed_when: false

    - name: 显示配置
      debug:
        msg: "终端 {{ inventory_hostname }} 已加入 Tailscale"
```

### 3.6 服务暴露配置

为了使终端访问更简便，可以在 MES 服务器上配置 Tailscale Serve，将内部端口映射到 Tailscale 域名：

```bash
# 暴露 mes-web（端口 5138）到 Tailscale
tailscale serve --bg --https=443 5138

# 暴露 mes-api（端口 5040）到 Tailscale
tailscale serve --bg --https=8443 5040
```

此后，终端可以通过 Tailscale MagicDNS 直接访问：

```
https://mes-server.tailnet-name.ts.net/    → mes-web
https://mes-server.tailnet-name.ts.net:8443 → mes-api
```

### 3.7 费用估算

| 用户类型 | 数量 | 产品 | 单价 | 月费用 |
|---------|------|------|------|--------|
| 车间终端 | 20 | Tailscale Standard | $8/设备/月 | $160 |
| 运维人员 | 5 | Tailscale Standard | $8/用户/月 | $40 |
| **总计** | **25** | | | **$200/月** |

> **注意：** Tailscale 的商业许可要求不允许在商业环境中使用免费个人版。免费版仅限 3 个用户，且不可用于商业用途。
>
> **替代方案：** 如果预算是主要限制，可以考虑 Headscale（自托管）方案。

---

## 4. 方案 B：Headscale（自托管）

### 4.1 适用场景

当以下条件**同时满足**时，考虑 Headscale：

- **数据主权要求：** 不允许任何 MES 通信数据离开厂区网络
- **终端数量 > 50：** 长期成本考虑
- **有 IT 资源：** 可以维护一台控制服务器（2 核 4GB 即可）
- **接受社区支持：** Headscale 是社区项目，非商业 SLA

### 4.2 部署架构

```
┌────────────────── 厂区网络 ──────────────────────┐
│                                                    │
│  ┌─────────┐       ┌──────────┐       ┌─────────┐ │
│  │Headscale│       │ MES 服务  │       │工位终端  │ │
│  │控制器    │←─────→│器        │←─────→│× 20     │ │
│  │10.0.1.5 │       │(Subnet   │       │         │ │
│  │:8080    │       │ Router)  │       │tailscale│ │
│  └─────────┘       └──────────┘       │客户端    │ │
│       │                                └─────────┘ │
│       │                                              │
│       └──────────── ← DERP Relay ──────────────────┘ │
│                 （NAT 穿透中继）                       │
└──────────────────────────────────────────────────────┘
```

### 4.3 部署步骤

#### 4.3.1 安装 Headscale 控制器

在厂区内一台独立 Linux 服务器上（或与 MES 服务器共用，如资源足够）：

```bash
# 1. 下载最新版本
#    查看 https://github.com/juanfont/headscale/releases 获取最新版本
HEADSCALE_VERSION="0.23.0"
wget https://github.com/juanfont/headscale/releases/download/v${HEADSCALE_VERSION}/headscale_${HEADSCALE_VERSION}_linux_amd64.deb

# 2. 安装
sudo dpkg -i headscale_${HEADSCALE_VERSION}_linux_amd64.deb

# 3. 配置（见下文）
sudo vim /etc/headscale/config.yaml

# 4. 启动
sudo systemctl enable --now headscale

# 5. 验证
sudo systemctl status headscale
```

#### 4.3.2 基本配置

编辑 `/etc/headscale/config.yaml`：

```yaml
# Headscale 核心配置
server_url: http://10.0.1.5:8080  # Headscale 服务地址
listen_addr: 0.0.0.0:8080

# DNS 配置
dns_config:
  magic_dns: true
  base_domain: mes.internal  # 终端使用 *.mes.internal 域名
  nameservers:
    - 10.0.1.1  # 厂区 DNS

# 数据库
db_type: sqlite3
db_path: /var/lib/headscale/db.sqlite

# 日志
log:
  level: info

# ACL
acl_policy_path: /etc/headscale/acl.hujson

# DERP（NAT 穿透中继）
derp:
  server:
    enabled: true  # 启用内置 DERP 中继
    region_id: 901
    region_code: "mes-factory"
    region_name: "AutoMES Factory DERP"
```

#### 4.3.3 注册节点

```bash
# 1. 创建命名空间（类似 Tailscale 的 Tailnet）
headscale namespaces create automes

# 2. 生成预认证密钥（供终端批量注册）
headscale preauthkeys create --namespace automes --expiration 720h --tags tag:operator

# 3. 注册 MES 服务器（Subnet Router）
#    在 MES 服务器上：
tailscale up --login-server=http://10.0.1.5:8080 \
  --advertise-routes=10.100.0.0/24 \
  --accept-routes

#    在 Headscale 服务端批准路由：
headscale routes enable -i <node-id> -r "10.100.0.0/24"

# 4. 注册工位终端
#    在终端上：
tailscale up --login-server=http://10.0.1.5:8080 \
  --authkey=<预认证密钥> \
  --accept-routes
```

#### 4.3.4 ACL 配置

创建 `/etc/headscale/acl.hujson`：

```json
{
  "groups": {
    "group:operator":  ["tag:operator"],
    "group:engineer":  ["tag:engineer"],
    "group:admin":     ["tag:admin"]
  },
  "acls": [
    // 操作员只能访问 MES 服务
    {"action": "accept", "src": ["group:operator"], "dst": ["tag:mes-server:*"]},
    // 工程师可以 SSH
    {"action": "accept", "src": ["group:engineer"], "dst": ["tag:mes-server:22", "tag:mes-server:80", "tag:mes-server:443"]},
    // 管理员完全访问
    {"action": "accept", "src": ["group:admin"],    "dst": ["*:*"]},
    // 终端间隔离
    {"action": "accept", "src": ["group:operator"], "dst": ["group:operator:*"], "proto": "icmp"}
  ],
  "tagOwners": {
    "tag:mes-server": ["group:admin"],
    "tag:operator":   ["group:admin"],
    "tag:engineer":   ["group:admin"],
    "tag:admin":      ["group:admin"]
  }
}
```

#### 4.3.5 Headscale 运维命令

```bash
# ── 节点管理 ──
headscale nodes list                    # 列出所有节点
headscale nodes delete -i <id>          # 移除节点
headscale nodes tag -i <id> -t tag:operator  # 打标签

# ── 路由管理 ──
headscale routes list                   # 查看子网路由
headscale routes enable -i <id> -r "10.100.0.0/24"  # 启用路由

# ── 密钥管理 ──
headscale preauthkeys list --namespace automes  # 列出预认证密钥
headscale preauthkeys expire -i <key-id> --namespace automes  # 过期密钥

# ── 命名空间管理 ──
headscale namespaces list
headscale namespaces create <name>

# ── 系统维护 ──
sudo systemctl restart headscale
sudo journalctl -u headscale -f        # 实时日志
```

### 4.4 Headscale 注意事项

| 关注点 | 说明 | 缓解措施 |
|--------|------|---------|
| **高可用** | Headscale 是单点故障 | 配置 PostgreSQL 后端 + 备用实例 |
| **升级风险** | 社区项目，API 可能不兼容 | 升级前备份数据库，测试环境验证 |
| **DERP 中继** | NAT 穿透需要 DERP 中继 | 已在配置中启用内置 DERP |
| **监控** | 无内置告警 | 配置 systemd 监控 + 端口监控 |
| **备份** | SQLite 数据库 | 每日备份 `/var/lib/headscale/db.sqlite` |

---

## 5. 方案 C：纯 WireGuard（简单场景）

### 5.1 适用场景

- 仅需少量固定终端接入（≤ 5 个）
- MES 服务器有固定公网 IP 或 NAT 端口转发
- 不需要集中 ACL 管理

### 5.2 服务器端配置

```bash
# 1. 安装 WireGuard
sudo apt install wireguard

# 2. 生成服务器密钥
wg genkey | tee /etc/wireguard/server.key | wg pubkey > /etc/wireguard/server.pub
chmod 600 /etc/wireguard/server.key

# 3. 创建配置文件 /etc/wireguard/wg0.conf
sudo tee /etc/wireguard/wg0.conf << 'EOF'
[Interface]
Address = 10.200.0.1/24
ListenPort = 51821
PrivateKey = <SERVER_PRIVATE_KEY>

# 启用 IP 转发
PostUp = iptables -A FORWARD -i wg0 -j ACCEPT; iptables -t nat -A POSTROUTING -o eth0 -j MASQUERADE
PostDown = iptables -D FORWARD -i wg0 -j ACCEPT; iptables -t nat -D POSTROUTING -o eth0 -j MASQUERADE

# 工位终端 1
[Peer]
PublicKey = <TERMINAL1_PUB_KEY>
AllowedIPs = 10.200.0.2/32

# 工位终端 2
[Peer]
PublicKey = <TERMINAL2_PUB_KEY>
AllowedIPs = 10.200.0.3/32
EOF

# 4. 启动 WireGuard
sudo systemctl enable --now wg-quick@wg0

# 5. 验证
sudo wg show
```

### 5.3 终端配置

```bash
# 1. 生成终端密钥
wg genkey | tee /etc/wireguard/client.key | wg pubkey > /etc/wireguard/client.pub

# 2. 创建配置文件 /etc/wireguard/wg0.conf
sudo tee /etc/wireguard/wg0.conf << 'EOF'
[Interface]
Address = 10.200.0.2/24
PrivateKey = <TERMINAL_PRIVATE_KEY>
# 可选：使用特定 DNS
DNS = 10.0.1.1

[Peer]
PublicKey = <SERVER_PUBLIC_KEY>
Endpoint = <服务器公网IP或域名>:51821
AllowedIPs = 10.200.0.0/24, 10.100.0.0/24  # 允许访问 MES mesh 网段
PersistentKeepalive = 25  # NAT 穿透保活
EOF

# 3. 在服务器端添加终端公钥
sudo wg set wg0 peer <TERMINAL_PUB_KEY> allowed-ips 10.200.0.2/32

# 4. 启动
sudo systemctl enable --now wg-quick@wg0
```

### 5.4 将所有容器加入 WireGuard 网络

如果希望 Docker 容器可以通过 WireGuard 访问，需要在 docker-compose.yaml 中添加网络配置：

```yaml
services:
  mes-web:
    networks:
      - default
      - wireguard

networks:
  wireguard:
    external: true
    name: wg_network
```

或者通过端口映射直接暴露到 WireGuard 接口：

```bash
# 将 API 端口绑定到 WireGuard 接口 IP
# 注意：需要修改 compose.yaml 中 mes-api 的 ports 配置
```

---

## 6. 安全最佳实践

### 6.1 安全基线

| 措施 | 重要性 | 说明 |
|------|--------|------|
| 工位终端隔离 | 🔴 强制 | 终端之间不能互相通信（ACL 限制） |
| 最小权限 | 🔴 强制 | 终端只能访问 MES 服务端口 |
| 密钥轮换 | 🟠 建议 | 预认证密钥定期更换 |
| 审计日志 | 🟠 建议 | 记录所有终端连接历史 |
| 设备认证 | 🔴 强制 | 不使用共享密钥，每设备独立密钥 |
| 防火墙 | 🟠 建议 | 限制 Tailscale/Headscale 管理端口 |
| 更新管理 | 🟠 建议 | Tailscale 客户端保持自动更新 |

### 6.2 ACL 最小权限原则

```
┌──────────────┐    ┌──────────────┐    ┌──────────────┐
│  产线操作员    │    │  质量工程师    │    │  系统管理员    │
├──────────────┤    ├──────────────┤    ├──────────────┤
│ mes-web:80   │    │ mes-web:80   │    │ mes-web:80   │
│ mes-api:5040 │    │ mes-api:5040 │    │ mes-api:5040 │
│              │    │ SSH:22       │    │ SSH:22       │
│  ✗ 其他终端   │    │ 数据库:5432  │    │ 数据库:5432  │
│  ✗ SSH       │    │              │    │ *:*          │
│  ✗ 数据库    │    │              │    │              │
└──────────────┘    └──────────────┘    └──────────────┘
```

### 6.3 防火墙规则

在 MES 服务器上加固 WireGuard/Tailscale 安全：

```bash
# 仅允许 WireGuard 端口
sudo ufw allow 51820/udp comment 'Uncloud WireGuard Mesh'
sudo ufw allow 51821/udp comment 'Terminal WireGuard VPN'

# 限制 Tailscale/Headscale 管理端口
sudo ufw allow from 10.0.0.0/8 to any port 8080 proto tcp comment 'Headscale admin internal'
sudo ufw allow from 100.64.0.0/10 to any port 8080 proto tcp comment 'Tailscale admin internal'

# 不允许终端 SSH（仅运维人员通过 VPN 的 ACL 控制）
# SSH 端口仅在 WireGuard/Tailscale 接口上监听
```

### 6.4 审计日志

**Tailscale 审计日志（商业版）：**

```bash
# 从控制台导出日志
# Settings → Logs → Export logs
# 或通过 API 获取
tailscale status --json | jq '.Peer'
```

**Headscale 审计日志：**

```bash
# 查看系统日志
sudo journalctl -u headscale --since "24 hours ago" | grep -E "(register|login|node)"

# 查看节点变更
sudo journalctl -u headscale | jq '. | select(.msg | contains("node"))'
```

---

## 7. 运维命令速查

### Tailscale

| 命令 | 说明 |
|------|------|
| `tailscale status` | 查看网络状态和在线设备 |
| `tailscale up` | 启动并加入 Tailnet |
| `tailscale down` | 断开 Tailnet 连接 |
| `tailscale logout` | 登出并移除设备密钥 |
| `tailscale serve --bg --https=443 5138` | 暴露本地端口到 Tailnet |
| `tailscale funnel --bg 5138` | 暴露服务到公网（⚠️ 谨慎） |
| `tailscale ip -4` | 查看本机 Tailscale IP |
| `tailscale ping 100.x.x.x` | 测试到目标节点的连通性 |
| `tailscale version` | 查看版本 |
| `tailscale set --accept-routes=true` | 启用接受子网路由 |

### WireGuard

| 命令 | 说明 |
|------|------|
| `sudo wg show` | 查看所有 WireGuard 接口状态 |
| `sudo wg show wg0` | 查看指定接口详情 |
| `sudo wg genkey` | 生成新私钥 |
| `sudo wg set wg0 peer <pubkey> remove` | 移除对等体 |
| `sudo systemctl restart wg-quick@wg0` | 重启 WireGuard |
| `sudo wg-quick up wg0` | 启动 WireGuard 接口 |
| `sudo wg-quick down wg0` | 停止 WireGuard 接口 |

---

## 8. 故障排查

### 8.1 终端无法连接

```bash
# 检查 Tailscale 状态
tailscale status                    # 确认 Connected
tailscale ping 100.x.x.1           # 测试到 MES 服务器的连通性

# 常见问题原因：
# 1. 防火墙阻止 51820/udp  → 检查 ufw 规则
# 2. 子网路由未批准         → 在控制台启用 Subnet Route
# 3. ACL 规则限制了访问     → 检查 ACL 配置
# 4. DERP 中继不可用        → tailscale netcheck 检查 NAT 类型
```

### 8.2 能 ping 通但无法访问 Web 服务

```bash
# 检查端口可达性
nc -zv 100.x.x.1 5138              # Web 端口
nc -zv 100.x.x.1 5040              # API 端口

# 检查 ACL 是否限制了端口
# → 确认 ACL 规则中允许目标端口

# 检查 Subnet Router 是否正确通告
# → 在控制台检查 mes-server 的路由状态
```

### 8.3 NAT 穿透失败

```bash
# 查看 NAT 类型
tailscale netcheck
# 预期：至少有一个 DERP 节点可用（Relay）

# 如果 DERP 不可用：
# Tailscale：自动选择最近的 DERP 节点
# Headscale：检查内置 DERP 服务是否运行
sudo systemctl status headscale
# 查看 DERP 相关日志
sudo journalctl -u headscale | grep derp
```

### 8.4 Headscale 数据库损坏

```bash
# 从备份恢复
sudo systemctl stop headscale
cp /var/lib/headscale/db.sqlite /var/lib/headscale/db.sqlite.corrupt
cp /backup/headscale/20260707/db.sqlite /var/lib/headscale/db.sqlite
sudo systemctl start headscale

# 验证
headscale nodes list
```

### 8.5 终端更换/退役

```bash
# Tailscale：在控制台移除设备
# 或通过 CLI：
tailscale logout  # 在终端上执行

# Headscale：
headscale nodes delete -i <node-id>

# WireGuard：
# 在服务器上：
sudo wg set wg0 peer <TERMINAL_PUB_KEY> remove
# 或在配置文件中注释掉对应 [Peer] 段
```

### 8.6 预认证密钥泄漏

```bash
# Tailscale：在控制台立即禁用密钥
# Settings → Keys → Disable

# Headscale：
headscale preauthkeys expire -i <key-id> --namespace automes
# 然后生成新密钥
headscale preauthkeys create --namespace automes --expiration 720h
```

---

## 附录 A：一键部署脚本

### A.1 Tailscale 批量部署（Windows 终端）

将此脚本保存为 `deploy-tailscale.ps1`，通过 SCCM/组策略下发：

```powershell
<#
.SYNOPSIS
    批量部署 Tailscale 到车间工位终端
.DESCRIPTION
    无交互安装，使用预认证密钥自动加入 Tailscale 网络。
    适用于 Windows 10/11 工位终端。
.PARAMETER AuthKey
    Tailscale 预认证密钥（在控制台生成）
.PARAMETER Hostname
    终端在 Tailscale 中的显示名称（可选）
.EXAMPLE
    .\deploy-tailscale.ps1 -AuthKey "tskey-auth-xxxxx"
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$AuthKey,
    [string]$Hostname = "line-$env:COMPUTERNAME"
)

$ErrorActionPreference = "Stop"
$logFile = "$env:TEMP\tailscale-deploy.log"

function Write-Log {
    param([string]$Message)
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "$timestamp $Message" | Out-File -Append -FilePath $logFile
    Write-Host "$timestamp $Message"
}

Write-Log "=== Tailscale 部署开始 ==="
Write-Log "主机名: $Hostname"

# 1. 检查是否已安装
$tailscalePath = "C:\Program Files\Tailscale\tailscale.exe"
if (Test-Path $tailscalePath) {
    Write-Log "Tailscale 已安装，跳过安装步骤"
} else {
    Write-Log "下载 Tailscale 安装包..."
    $installer = "$env:TEMP\tailscale-setup.exe"
    try {
        Invoke-WebRequest -Uri "https://pkgs.tailscale.com/stable/tailscale-setup-latest-amd64.exe" -OutFile $installer
        Write-Log "安装 Tailscale..."
        Start-Process -Wait -FilePath $installer -ArgumentList "/S" -NoNewWindow
        Start-Sleep -Seconds 5
    } catch {
        Write-Log "❌ 安装失败: $_"
        exit 1
    }
}

# 2. 加入网络
Write-Log "加入 Tailscale 网络..."
try {
    & $tailscalePath up --authkey=$AuthKey --accept-routes --hostname=$Hostname
    Start-Sleep -Seconds 10
} catch {
    Write-Log "❌ 认证失败: $_"
    exit 1
}

# 3. 验证
$status = & $tailscalePath status
Write-Log "Tailscale 状态: $status"

if ($status -match "Connected") {
    Write-Log "✅ Tailscale 部署成功"
} else {
    Write-Log "⚠️ 状态检查未确认连接，请手动验证"
}

Write-Log "=== Tailscale 部署完成 ==="
```

### A.2 一键部署 MES 服务器端

```bash
#!/bin/bash
# setup-mes-vpn.sh — MES 服务器 VPN 一键配置
# 用法: ./setup-mes-vpn.sh [tailscale|headscale|wireguard]

set -euo pipefail

MODE="${1:-tailscale}"
MESH_NET="10.100.0.0/24"

echo "=== AutoMES VPN 服务器端配置 ==="
echo "模式: $MODE"

case "$MODE" in
  tailscale)
    echo "1/3: 安装 Tailscale..."
    curl -fsSL https://tailscale.com/install.sh | sh

    echo "2/3: 启动并通告子网路由..."
    tailscale up \
      --advertise-routes="$MESH_NET" \
      --accept-routes \
      --hostname=mes-server

    echo "3/3: 请在控制台启用子网路由:"
    echo "     https://login.tailscale.com/admin/machines"
    echo "     找到 mes-server → Edit route settings → Enable 10.100.0.0/24"
    ;;

  headscale)
    echo "1/3: 安装 Tailscale 客户端..."
    curl -fsSL https://tailscale.com/install.sh | sh

    echo "2/3: 配置 Headscale 服务器地址..."
    read -p "Headscale 服务器地址 (例如 http://10.0.1.5:8080): " HEADSCALE_URL

    tailscale up \
      --login-server="$HEADSCALE_URL" \
      --advertise-routes="$MESH_NET" \
      --accept-routes \
      --hostname=mes-server

    echo "3/3: 请在 Headscale 服务端批准路由:"
    echo "     headscale routes enable -i <node-id> -r $MESH_NET"
    ;;

  wireguard)
    echo "1/3: 安装 WireGuard..."
    apt update && apt install -y wireguard

    echo "2/3: 生成密钥..."
    cd /etc/wireguard
    umask 077
    wg genkey | tee server.key | wg pubkey > server.pub

    echo "3/3: 配置文件已生成，请手动编辑 /etc/wireguard/wg0.conf"
    echo "     参考 docs/deployment/terminal-access.md §5"
    ;;

  *)
    echo "用法: $0 [tailscale|headscale|wireguard]"
    exit 1
    ;;
esac

echo "=== 完成 ==="
```

---

## 附录 B：相关文档

| 文档 | 路径 |
|------|------|
| 生产部署指南 | `docs/deployment/uncloud-setup.md` |
| PostgreSQL 备份 | `docs/deployment/postgres-backup.md` |
| Tailscale 官方文档 | https://tailscale.com/docs/ |
| Headscale GitHub | https://github.com/juanfont/headscale |
| WireGuard 官方 | https://www.wireguard.com/ |
