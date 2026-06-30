# AutoMES — 博世 ESP® 制动系统 MES

> 面向汽车 Tier-1 供应商（博世 ESP® 电子稳定程序制动系统总成产线）的全链路制造执行系统。
> 覆盖 **工单 → 物料 → SPC → 追溯 → Andon → OEE**，7 站 31 工序全 Saga 编排，热路径零堆分配。

## 技术栈

| 类别 | 技术 | 版本 |
|------|------|------|
| 运行时 | .NET SDK | 10.0 |
| UI | MudBlazor | 9.6.0 |
| 序列化 | MemoryPack | 1.21.4 |
| 工作流 | Cleipnir.ResilientFunctions | 4.2.5 |
| 消息（进程内） | MessagePipe | 1.8.2 |
| 响应式 | R3 | 1.3.1 |
| 日志 | ZLogger | 2.5.10 |
| 主键 | Ulid | 1.4.1 |
| UI 集合 | ObservableCollections | 3.3.4 |
| 缓存 | MasterMemory | 3.0.4 |
| 数据库 | Npgsql EF Core + PostgreSQL 17 | 10.0.2 |
| 实时通信 | SignalR (MemoryPack 二进制) | ASP.NET Core 10 |
| IO 管道 | System.Threading.Channels / System.IO.Pipelines | .NET 10 |

## 项目结构

```
src/
├── MesAdmin.Domain/          # 领域模型 + MemoryPack + Ulid
├── MesAdmin.Application/     # Cleipnir Saga + 业务接口
├── MesAdmin.Infrastructure/  # EF Core + SignalR + PLC + Channels + Pipelines
├── MesAdmin.Web/             # MudBlazor 管理后台
└── MesAdmin.Api/             # REST API (Avalonia 工位终端用)
```

**依赖方向：** `Web → Infrastructure → Application → Domain` ← `API → Infrastructure`

## 部署

- **本地开发**：[OrbStack](https://orbstack.dev) + `docker compose -f docker/compose.dev.yaml up -d`
- **生产部署**：[Uncloud](https://uncloud.run)（WireGuard mesh + Caddy + 多机 Docker Compose）

## 快速开始

```bash
# 启动 PostgreSQL
docker compose -f docker/compose.dev.yaml up -d

# 运行 Web
dotnet run --project src/MesAdmin.Web
```

## 文档

| 文档 | 路径 |
|------|------|
| AI 编程助手指南（宪法） | [AGENTS.md](./AGENTS.md) |
| 任务清单 | [TASKS.md](./TASKS.md) |
| PRD v2（博世ESP版） | `automotive-mes-prd-v2.html` |
| TAD v2（零分配架构） | `automotive-mes-tad-v2.html` |
