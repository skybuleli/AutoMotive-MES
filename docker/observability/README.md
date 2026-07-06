# AutoMES 可观察性栈

这是 Phase 1 栈：

```text
GreptimeDB + vmalert + Alertmanager
```

这里有意不包含对象存储、Grafana、OpenTelemetry Collector 和主机/数据库 exporter。

## 启动

```bash
docker compose -f docker/observability/compose.yaml up -d
```

本地端点：

- GreptimeDB HTTP and OTLP: http://localhost:4000
- GreptimeDB MySQL protocol: localhost:4002
- GreptimeDB PostgreSQL protocol: localhost:4003
- vmalert: http://localhost:8880
- Alertmanager: http://localhost:9093

## AutoMES 配置

把 OpenTelemetry exporter 指向 GreptimeDB：

```text
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4000/v1/otlp/v1/metrics
OTEL_SERVICE_NAME=MesAdmin.Api
OTEL_RESOURCE_ATTRIBUTES=deployment.environment=dev,service.namespace=AutoMES
```

具体 .NET 接入在 AutoMES 应用代码里完成。Phase 1 只埋以下信号：

- Saga 和工单链路
- PLC 采集与 Channel 健康
- OEE、Andon、SignalR 推送健康

## 飞书告警

Alertmanager 发送到本地 API 适配器：

```text
http://host.docker.internal:5040/internal/alerts/feishu
```

测试通知前先启动 API：

```bash
dotnet run --project src/MesAdmin.Api
```

API 需要配置飞书机器人地址；如果机器人开启了签名校验，同时配置 Secret：

```text
Alerts__Feishu__WebhookUrl=https://open.feishu.cn/open-apis/bot/v2/hook/...
Alerts__Feishu__Secret=...
```

适配器负责接收 Alertmanager payload，格式化飞书消息，然后调用 API 环境变量中配置的飞书机器人 webhook。

## 验证

```bash
docker compose -f docker/observability/compose.yaml ps
curl -fsS http://localhost:4000/health
curl -fsS http://localhost:8880/health
curl -fsS http://localhost:9093/-/healthy
```

AutoMES 开始导出数据后，再用样例指标触发规则评估。

## 文件

- `compose.yaml`: 本地 Phase 1 服务
- `vmalert-rules.yaml`: MES 告警规则
- `alertmanager.yaml`: 到 AutoMES 飞书适配器的通知路由

## Phase 1 跳过

- 对象存储：GreptimeDB 先使用本地持久卷。
- Collector：应用直接用 OTLP 写入 GreptimeDB。
- Grafana：GreptimeDB UI 和查询能力先够验证。
- 日志后端：第一阶段使用 ZLogger 控制台日志 + trace id。
- Exporters：先做应用侧 DB 和健康指标。
