# AutoMES 可观察性方案决策

日期：2026-07-06
状态：Phase 1 已接受

## 决策

Phase 1 使用小型开源栈：

```text
AutoMES OTLP -> GreptimeDB
vmalert -> GreptimeDB PromQL API
vmalert -> Alertmanager
Alertmanager -> MesAdmin.Api /internal/alerts/feishu -> 飞书
```

GreptimeDB 在 Phase 1 使用本地持久卷。对象存储延后。

## 原因

- GreptimeDB 接收 OTLP metrics/traces，并暴露 PromQL-compatible 查询接口。
- vmalert 只做规则评估，不负责 scrape，也不维护本地 TSDB；对当前需求比 Prometheus 更轻。
- Alertmanager 负责告警分组、去重、重复发送间隔和通知路由。
- Alertmanager webhook payload 不是飞书机器人消息格式，所以需要一个很薄的 MesAdmin.Api 适配端点。

## 范围

Phase 1 只观测能直接解释生产故障的信号：

- Saga 和工单链路
- PLC 采集与 Channel 健康
- OEE、Andon、SignalR 推送健康

暂不建设完整基础设施监控平台。

## 延后

- GreptimeDB 对象存储后端
- OpenTelemetry Collector
- Grafana 或 Perses dashboard 服务
- Loki/OpenObserve/log 检索后端
- postgres_exporter, node_exporter, cAdvisor
- 阈值管理 UI
- 告警历史表或通知审计表

只有 Phase 1 数据证明需要时才添加。

## 数据归属

GreptimeDB 存储的是可观察性副本，不是 MES 业务事实。

PostgreSQL 仍然是工单、追溯、OEE 业务记录、Andon 事件、质量记录和审计状态的事实源。

## Label 规范

允许的 metric labels：

```text
env
line
station
equipment_code
operation_code
severity
result
```

禁止的 metric labels：

```text
order_id
vin
serial_no
material_batch
component_batch
operator_id
exception_message
plc_tag
connection_id
```

高基数字段只能进入 traces 或结构化日志。

## 留存

初始留存目标：

- metrics: 30 days
- traces: 7 days
- container logs: 7 到 14 天，通过容器日志轮转控制

质量图片、附件、追溯记录和审计记录不进入可观察性存储。

## 验收

Phase 1 通过以下检查才算完成：

- GreptimeDB 使用本地卷，容器重启后数据仍在。
- vmalert 可以查询 GreptimeDB，并触发测试告警。
- Alertmanager 可以把告警发送到 `MesAdmin.Api`。
- `MesAdmin.Api` 可以把格式化后的告警转发到飞书。
- Saga、PLC、OEE/Andon、SignalR 至少各有一个可查询指标。
- 没有 metric 使用被禁止的高基数 label。
