# AutoMES PostgreSQL 备份策略

> **对应任务：** TD.6（PostgreSQL 3 级备份）
> **核心原则：** 3-2-1 备份法则（3 份副本、2 种介质、1 份异地）

## 目录

1. [备份架构总览](#1-备份架构总览)
2. [第 1 级：每日全量备份（pg_dump）](#2-第-1-级每日全量备份pg_dump)
3. [第 2 级：持续 WAL 归档（PITR）](#3-第-2-级持续-wal-归档pitr)
4. [第 3 级：每周全量 + 异地存储](#4-第-3-级每周全量--异地存储)
5. [配置 PostgreSQL 开启归档模式](#5-配置-postgresql-开启归档模式)
6. [恢复操作指南](#6-恢复操作指南)
7. [备份验证与监控](#7-备份验证与监控)
8. [运维命令速查](#8-运维命令速查)
9. [常见问题](#9-常见问题)

---

## 1. 备份架构总览

### 3 级备份策略

```
┌─────────────────────────────────────────────────────────────────┐
│                      3 级备份策略                                 │
│                                                                   │
│  第 1 级：每日全量 (pg_dump)         → 恢复时间: < 2h            │
│  第 2 级：持续 WAL 归档 (PITR)      → 恢复时间: < 30min         │
│  第 3 级：异地备份 (对象存储)       → 恢复时间: < 4h            │
└─────────────────────────────────────────────────────────────────┘
```

### 备份时间线

```
Mon       Tue       Wed       Thu       Fri       Sat       Sun
│         │         │         │         │         │         │
├─ pg_dump ┼─ pg_dump ┼─ pg_dump ┼─ pg_dump ┼─ pg_dump ┼─ pg_dump ┼─
│         │         │         │         │         │         │
└─────────┴─────────┴─────────┴─────────┴─────────┴─────────┴─────────
         WAL 持续归档 (每 5 分钟)
         └────────────────────────────────────────────────────────┘
                                    ↑
                              Sun: 全量 + 异地推送
```

### RPO 与 RTO

| 指标 | 第 1 级 | 第 2 级 | 第 3 级 |
|------|---------|---------|---------|
| **RPO**（数据丢失上限） | 24 小时 | 5 分钟 | 7 天 |
| **RTO**（恢复时间目标） | 1-2 小时 | 15-30 分钟 | 2-4 小时 |
| **存储位置** | 本地卷 `pg-backup-data` | 本地卷 `postgres-wal` | 异地对象存储 |
| **保留时间** | 7 天 | 14 天 | 30 天 |

---

## 2. 第 1 级：每日全量备份（pg_dump）

### 2.1 现有配置（docker/compose.yaml）

`pg-backup` 服务已在 `docker/compose.yaml` 中定义，运行在 PostgreSQL 所在机器上：

```yaml
services:
  pg-backup:
    image: postgres:17-alpine
    container_name: automes-pg-backup
    environment:
      PGHOST: postgres
      PGPORT: 5432
      PGDATABASE: automes
      PGUSER: mes
      PGPASSWORD: ${POSTGRES_PASSWORD:?必须设置 POSTGRES_PASSWORD}
      BACKUP_DIR: /backups
      RETENTION_DAYS: 7
      TZ: Asia/Shanghai
    volumes:
      - pg-backup-data:/backups
      - postgres-wal:/wal_archive:ro
    depends_on:
      postgres:
        condition: service_healthy
    restart: unless-stopped
```

### 2.2 备份调度

- **频率：** 每日 03:00（北京时间）
- **格式：** `pg_dump -Fc`（自定义格式，支持压缩 + 并行恢复）
- **保留：** 7 天（自动清理过期备份）
- **文件命名：** `automes-YYYYMMDD-HHMMSS.dump`

### 2.3 验证备份完整性

```bash
# 列出所有备份文件
uc exec pg-backup -- ls -lah /backups/

# 预期输出：
# -rw-r--r--  automes-20260707-030000.dump  156 MB
# -rw-r--r--  automes-20260706-030000.dump  148 MB
# -rw-r--r--  automes-20260705-030000.dump  152 MB

# 验证备份文件完整性
uc exec pg-backup -- pg_restore -l /backups/automes-20260707-030000.dump | head -20
# 预期：列出备份中的数据库对象（表、索引、数据）
```

### 2.4 手动触发备份

```bash
# 手动触发即时备份
uc exec pg-backup -- pg_dump -Fc -h postgres -U mes -d automes \
  -f /backups/automes-manual-$(date +%Y%m%d-%H%M%S).dump

# 查看备份文件大小
uc exec pg-backup -- du -sh /backups/
```

### 2.5 备份文件大小预估

| 数据量 | pg_dump 大小 | 每日备份 | 7 天总量 | 磁盘要求 |
|--------|-------------|---------|---------|---------|
| 50 MB（初始种子数据） | ~10 MB | 10 MB | 70 MB | < 1 GB |
| 500 MB（1 个月运行） | ~100 MB | 100 MB | 700 MB | ~2 GB |
| 2 GB（6 个月运行） | ~400 MB | 400 MB | 2.8 GB | ~5 GB |
| 10 GB（2 年运行） | ~2 GB | 2 GB | 14 GB | ~20 GB |

> **建议：** `pg-backup-data` 卷至少分配 10 GB 空间。

---

## 3. 第 2 级：持续 WAL 归档（PITR）

### 3.1 什么是 PITR

**时间点恢复（Point-In-Time Recovery）** 允许将数据库恢复到任意时间点，而不仅仅是备份时间点。这对于恢复误操作（如 `DROP TABLE`）至关重要。

### 3.2 WAL 归档卷

`postgres-wal` 卷已在 `docker/compose.yaml` 中定义：

```yaml
volumes:
  postgres-wal:
    driver: local
```

此卷挂载到两个服务：
- **postgres**：`/var/lib/postgresql/wal_archive` — PostgreSQL 写入归档 WAL
- **pg-backup**：`/wal_archive:ro` — pg-backup 读取并管理归档 WAL

### 3.3 启用归档模式

PostgreSQL 默认**不启用**归档模式。需要在 PostgreSQL 容器中手动配置：

#### 方法 A：通过环境变量（推荐，但需定制 postgres 镜像）

创建一个简短的初始化脚本：

```bash
# docker/pg-init/configure-wal-archive.sh
#!/bin/bash
# 在 PostgreSQL 启动时执行，启用 WAL 归档

cat >> $PGDATA/postgresql.conf << EOF
# WAL 归档配置（PITR — TD.6）
wal_level = replica
archive_mode = on
archive_command = 'cp %p /var/lib/postgresql/wal_archive/%f'
archive_timeout = 300
max_wal_senders = 3
wal_keep_size = 1024
EOF
```

将此脚本挂载到 PostgreSQL 容器的 `/docker-entrypoint-initdb.d/` 目录：

```yaml
services:
  postgres:
    image: postgres:17-alpine
    volumes:
      - postgres-data:/var/lib/postgresql/data
      - postgres-wal:/var/lib/postgresql/wal_archive
      - ./pg-init:/docker-entrypoint-initdb.d:ro  # 添加初始化脚本
```

#### 方法 B：通过 SQL 在线启用（不重启）

```bash
# 连接到 PostgreSQL 并启用 WAL 归档
uc exec postgres -- psql -U mes -d automes -c "
ALTER SYSTEM SET wal_level = 'replica';
ALTER SYSTEM SET archive_mode = 'on';
ALTER SYSTEM SET archive_command = 'cp %p /var/lib/postgresql/wal_archive/%f';
ALTER SYSTEM SET archive_timeout = '300';
SELECT pg_reload_conf();
"

# 验证配置已生效
uc exec postgres -- psql -U mes -d automes -c "SHOW archive_mode;"
# 预期：on
```

> **注意：** 初次启用 `archive_mode` 需要重启 PostgreSQL。使用 `docker compose restart postgres` 或 `uc deploy` 更新部署。

### 3.4 验证 WAL 归档

```bash
# 查看已归档的 WAL 段
uc exec pg-backup -- ls -lah /wal_archive/ | tail -10

# 预期输出：大量 16MB 的 WAL 段文件
# -rw-------  000000010000000000000001  16 MB
# -rw-------  000000010000000000000002  16 MB

# 检查归档状态
uc exec postgres -- psql -U mes -d automes -c "
SELECT * FROM pg_stat_archiver;
"
# 预期：last_failed_wal 为空，表明归档成功
```

### 3.5 WAL 清理策略

WAL 段会持续产生（每 5 分钟或每 16MB）。需要定期清理：

```bash
# pg-backup 启动脚本中包含 WAL 清理逻辑：
# 保留最近 14 天的 WAL 段
# 在 pg-backup 容器中执行：
# find /wal_archive -name "0000*" -type f -mtime +14 -delete
```

---

## 4. 第 3 级：每周全量 + 异地存储

### 4.1 异地备份策略

第 3 级备份将每周全量备份推送到异地存储，防止厂区级灾难（火灾、洪水、盗窃）。

### 4.2 使用 rclone 推送（推荐）

`rclone` 是一个命令行文件同步工具，支持 40+ 存储后端。

#### 安装 rclone

```bash
# 在 pg-backup 容器中使用（或创建独立备份容器）
uc exec pg-backup -- apk add --no-cache rclone
```

#### 配置 rclone

```bash
# 配置远程存储（以 S3 兼容存储为例）
uc exec pg-backup -- rclone config

# 配置完成后，测试连接
uc exec pg-backup -- rclone ls remote:automes-backups
```

#### 每周推送脚本

在 `docker/pg-backup/` 目录下创建推送脚本：

```bash
#!/bin/bash
# sync-to-remote.sh — 将本地备份同步到异地存储
# 每周日 04:00 执行（在 pg-backup entrypoint 中添加调度）

REMOTE_NAME="${RCLONE_REMOTE_NAME:-s3}"
REMOTE_PATH="${RCLONE_REMOTE_PATH:-automes-backups}"
BACKUP_SOURCE="${BACKUP_DIR:-/backups}"

echo "=== $(date) 开始异地同步 ==="

# 扫描所有备份文件，计算 MD5 校验
for f in "$BACKUP_SOURCE"/automes-*.dump; do
  if [ -f "$f" ]; then
    md5sum "$f" > "$f.md5"
  fi
done

# 同步到远程存储（跳过已存在的文件）
rclone sync "$BACKUP_SOURCE" "$REMOTE_NAME:$REMOTE_PATH" \
  --checksum \
  --verbose \
  --retries 3

echo "=== 异地同步完成 ==="
```

### 4.3 支持的异地存储后端

| 存储后端 | rclone 名称 | 费用 | 适用场景 |
|---------|------------|------|---------|
| AWS S3 | `s3` | 按量付费 | 推荐：稳定性最高 |
| 阿里云 OSS | `oss` | 按量付费 | 推荐：国内延迟低 |
| 腾讯云 COS | `cos` | 按量付费 | 可选 |
| MinIO（自建） | `s3` | 免费 | 自建异地 MinIO 服务器 |
| SFTP | `sftp` | 免费 | 厂区另一台服务器 |
| WebDAV | `webdav` | 免费 | 内部 NAS 设备 |

### 4.4 环境变量

```bash
# 在 .env.production 中添加异地备份配置
RCLONE_REMOTE_NAME=s3
RCLONE_REMOTE_PATH=automes-backups
RCLONE_S3_PROVIDER=AWS
RCLONE_S3_ENDPOINT=https://s3.amazonaws.com
RCLONE_S3_REGION=ap-northeast-1
RCLONE_S3_ACCESS_KEY_ID=your-access-key
RCLONE_S3_SECRET_ACCESS_KEY=your-secret-key
```

### 4.5 异地备份验证

```bash
# 列出远程存储中的备份文件
uc exec pg-backup -- rclone ls s3:automes-backups

# 测试从远程恢复
uc exec pg-backup -- rclone copy s3:automes-backups/automes-20260707-030000.dump /tmp/test-restore/
uc exec pg-backup -- pg_restore -l /tmp/test-restore/automes-20260707-030000.dump | head -5
```

---

## 5. 配置 PostgreSQL 开启归档模式

### 5.1 一键启用 WAL 归档

在 PostgreSQL 容器中执行以下 SQL：

```bash
# 连接到 PostgreSQL
uc exec postgres -- psql -U mes -d automes

-- 在 psql 中执行：
ALTER SYSTEM SET wal_level = 'replica';
ALTER SYSTEM SET archive_mode = 'on';
ALTER SYSTEM SET archive_command = 'cp %p /var/lib/postgresql/wal_archive/%f';
ALTER SYSTEM SET archive_timeout = 300;  -- 每 5 分钟归档一次
ALTER SYSTEM SET max_wal_senders = 3;
ALTER SYSTEM SET wal_keep_size = 1024;   -- MB

-- 重新加载配置
SELECT pg_reload_conf();

-- 验证
SHOW archive_mode;
SHOW archive_command;
```

### 5.2 重启确认（仅首次）

```bash
# 首次启用 archive_mode 需要重启容器
uc deploy -f docker/compose.yaml -e .env.production

# 验证归档是否正常工作
uc exec postgres -- psql -U mes -d automes -c "
SELECT archived_count, failed_count, last_archived_wal, last_failed_wal
FROM pg_stat_archiver;
"
```

### 5.3 归档性能影响

| 配置 | 影响 | 说明 |
|------|------|------|
| `archive_timeout = 300` | 轻微 | 每 5 分钟产生一个 ~16MB WAL 段 |
| `wal_level = replica` | 轻微 | 默认值，额外记录少量 WAL 信息 |
| `max_wal_senders = 3` | 轻微 | 允许最多 3 个 WAL 接收者 |
| `wal_keep_size = 1024` | ~1GB 磁盘 | 保留最近 1GB 的 WAL 段 |

> **结论：** 对 MES 系统的性能影响可忽略（< 1% CPU/IO 开销）。

---

## 6. 恢复操作指南

### 6.1 从每日全量备份恢复（第 1 级）

**场景：** 数据库损坏但不需要精确时间点恢复。

```bash
# 1. 停止所有依赖服务（防止写入）
uc stop mes-api
uc stop mes-web

# 2. 找到最新的备份文件
LATEST_BACKUP=$(uc exec pg-backup -- ls -t /backups/automes-*.dump | head -1)
echo "恢复文件: $LATEST_BACKUP"

# 3. 创建新数据库（删除旧库）
uc exec postgres -- psql -U mes -d automes -c "
SELECT pg_terminate_backend(pg_stat_activity.pid)
FROM pg_stat_activity
WHERE pg_stat_activity.datname = 'automes'
  AND pid <> pg_backend_pid();
"
uc exec postgres -- psql -U mes -c "DROP DATABASE IF EXISTS automes;"
uc exec postgres -- psql -U mes -c "CREATE DATABASE automes;"

# 4. 从备份恢复
uc exec postgres -- pg_restore -U mes -d automes \
  -Fc -j 4 --verbose "$LATEST_BACKUP" 2>&1 | tail -5

# 5. 重新启动服务
uc start mes-api
uc start mes-web

# 6. 验证数据
curl -f http://mes-api:5040/health
```

### 6.2 PITR 时间点恢复（第 2 级）

**场景：** 恢复到特定时间点（如误删除前的瞬间）。

```bash
# 1. 停止服务，关闭当前数据库
uc stop mes-api mes-web
uc exec postgres -- pg_ctl -D $PGDATA stop

# 2. 备份当前数据目录（以防恢复失败）
uc exec postgres -- cp -r $PGDATA $PGDATA.bak

# 3. 清除数据目录，准备从基础备份恢复
uc exec postgres -- rm -rf $PGDATA/*
uc exec postgres -- pg_basebackup -D $PGDATA -X fetch

# 4. 创建 recovery.conf（或使用 recovery.signal）
uc exec postgres -- bash -c "
cat > $PGDATA/recovery.conf << EOF
restore_command = 'cp /var/lib/postgresql/wal_archive/%f %p'
recovery_target_time = '2026-07-07 14:30:00+08'
recovery_target_action = 'promote'
EOF
"

# 5. 启动 PostgreSQL（自动执行 PITR）
uc exec postgres -- pg_ctl -D $PGDATA start

# 6. 验证恢复完成
uc exec postgres -- psql -U mes -d automes -c "
SELECT pg_is_in_recovery();
"
# 预期：false（已 promote 为主库）

# 7. 清理 recovery.conf
uc exec postgres -- rm -f $PGDATA/recovery.conf

# 8. 重启服务
uc start mes-api mes-web
```

### 6.3 从异地备份恢复（第 3 级）

**场景：** 厂区服务器完全损毁，需要在新服务器上重建。

```bash
# 1. 在新服务器上初始化 Uncloud
uc machine init --name new-db root@<新服务器IP>

# 2. 在目标机器上创建卷
uc volume create postgres-data -m new-db
uc volume create postgres-wal -m new-db
uc volume create pg-backup-data -m new-db

# 3. 安装 rclone，从远程存储拉取备份
uc exec -m new-db -- apk add --no-cache rclone
uc exec -m new-db -- rclone copy s3:automes-backups/automes-weekly.dump /tmp/

# 4. 启动 PostgreSQL（使用 compose.yaml 中定义的服务）
# 修改 x-machines 指向新机器后部署
uc deploy -f docker/compose.yaml -e .env.production

# 5. 恢复数据
uc exec postgres -- pg_restore -U mes -d automes \
  -Fc -j 4 /tmp/automes-weekly.dump

# 6. 验证
uc exec postgres -- psql -U mes -d automes -c "SELECT COUNT(*) FROM production_orders;"
```

---

## 7. 备份验证与监控

### 7.1 每日验证（自动化）

在 `pg-backup` 的 entrypoint 中添加备份后的验证逻辑：

```bash
# 备份完成后自动验证
pg_restore -l "$BACKUP_FILE" > /dev/null 2>&1
if [ $? -eq 0 ]; then
  echo "✓ 备份完整性验证通过: $BACKUP_FILE"
else
  echo "✗ 备份损坏: $BACKUP_FILE" | tee /dev/stderr
  # 发送告警（通过健康检查失败触发 Uncloud 自动重启）
  exit 1
fi
```

### 7.2 监控指标

```bash
# 检查备份文件大小趋势
uc exec pg-backup -- du -sh /backups/*.dump | sort -k2

# 检查 WAL 归档延迟
uc exec postgres -- psql -U mes -d automes -c "
SELECT
  last_archived_wal,
  last_archived_time,
  archived_count,
  failed_count
FROM pg_stat_archiver;
"

# 检查磁盘使用率
uc exec pg-backup -- df -h /backups
uc exec postgres -- df -h /var/lib/postgresql/data
```

### 7.3 告警规则

| 指标 | 阈值 | 告警级别 | 处理方式 |
|------|------|---------|---------|
| 备份文件大小 | 异常缩小 >50% | 严重 | 检查备份脚本 |
| 备份文件缺失 | 超过 26 小时未更新 | 严重 | 手动触发备份 |
| WAL 归档失败 | `failed_count > 0` | 警告 | 检查磁盘空间 |
| 磁盘使用率 | >85% | 警告 | 清理或扩容 |
| 异地同步失败 | 连续 2 次失败 | 警告 | 检查远程存储 |

---

## 8. 运维命令速查

```bash
# ── 备份操作 ──
uc exec pg-backup -- pg_dump -Fc -U mes -d automes -f /backups/manual.dump
# 手动触发全量备份

uc exec pg-backup -- pg_restore -l /backups/automes-20260707.dump
# 验证备份文件完整性

uc exec pg-backup -- ls -lah /backups/
# 列出所有备份文件

# ── WAL 归档 ──
uc exec postgres -- psql -U mes -d automes -c "SHOW archive_mode;"
# 检查归档模式是否开启

uc exec postgres -- psql -U mes -d automes -c "SELECT * FROM pg_stat_archiver;"
# 查看归档状态

uc exec pg-backup -- ls -lah /wal_archive/ | wc -l
# 统计 WAL 段数量

# ── 异地同步 ──
uc exec pg-backup -- rclone ls s3:automes-backups/
# 列出远程备份文件

uc exec pg-backup -- rclone sync /backups s3:automes-backups/ --checksum
# 手动触发异地同步

# ── 恢复 ──
uc exec postgres -- pg_restore -U mes -d automes -Fc -j 4 /backups/automes-20260707.dump
# 从备份恢复（4 并行线程）

# ── 监控 ──
uc exec postgres -- psql -U mes -d automes -c "
SELECT pg_size_pretty(pg_database_size('automes')) AS db_size;
"
# 查看数据库大小

uc exec pg-backup -- df -h /backups
# 查看备份卷使用情况
```

---

## 9. 常见问题

### 备份文件比预期小很多

**原因：** 数据库连接失败，备份了空数据库。

**解决：**
```bash
# 检查备份文件是否有效
uc exec pg-backup -- pg_restore -l /backups/suspicious-small.dump | head -5
# 如果返回空或只返回注释行，说明备份无效

# 手动重新备份
uc exec pg-backup -- pg_dump -Fc -h postgres -U mes -d automes \
  -f /backups/automes-retry-$(date +%Y%m%d-%H%M%S).dump
```

### WAL 归档失败

```sql
-- 检查归档状态
SELECT * FROM pg_stat_archiver;
```

**可能原因：**
1. **磁盘空间不足** → 清理旧 WAL 或扩容
2. **目标目录权限错误** → 检查 `wal_archive` 目录权限
3. **归档命令错误** → 手动测试：`cp /var/lib/postgresql/data/pg_wal/test /var/lib/postgresql/wal_archive/test`

### 备份卷空间不足

```bash
# 临时清理过期备份（保留最近 3 天）
uc exec pg-backup -- find /backups -name "automes-*.dump" -mtime +3 -delete

# 检查释放后的空间
uc exec pg-backup -- df -h /backups
```

### 异地同步认证失败

```bash
# 检查 rclone 配置
uc exec pg-backup -- rclone config show

# 重新配置
uc exec pg-backup -- rclone config reconnect s3:

# 测试连接
uc exec pg-backup -- rclone about s3:
```

### PITR 恢复后数据不一致

**原因：** `recovery_target_time` 不精确，导致部分事务丢失或重放过多。

**解决：** 使用更精确的恢复目标：
```sql
-- 先使用 pg_waldump 查找精确的 LSN 或事务 ID
-- 然后使用 recovery_target_lsn 或 recovery_target_xid
recovery_target_lsn = '0/1234567'
```

---

## 附录 A：备份文件清单

| 文件 | 路径 | 格式 | 保留 | 说明 |
|------|------|------|------|------|
| 每日全量 | `/backups/automes-YYYYMMDD-HHMMSS.dump` | pg_dump -Fc | 7 天 | 第 1 级 |
| WAL 段 | `/wal_archive/00000001...` | 16MB 每段 | 14 天 | 第 2 级 |
| 每周全量 | `s3:automes-backups/automes-weekly-YYYYMMDD.dump` | pg_dump -Fc | 30 天 | 第 3 级 |
| MD5 校验 | `/backups/automes-*.dump.md5` | MD5 文本 | 同备份 | 完整性验证 |

## 附录 B：恢复时间预估

| 数据量 | pg_restore（-j 4） | 全流程（含验证） |
|--------|-------------------|----------------|
| 100 MB | ~30 秒 | ~2 分钟 |
| 500 MB | ~2 分钟 | ~5 分钟 |
| 2 GB | ~8 分钟 | ~15 分钟 |
| 10 GB | ~30 分钟 | ~60 分钟 |

## 附录 C：相关文档

| 文档 | 路径 |
|------|------|
| 生产 compose.yaml | `docker/compose.yaml` |
| 部署指南 | `docs/deployment/uncloud-setup.md` |
| 环境变量模板 | `docker/.env.production.example` |
| 部署任务清单 | `TASKS.md`（TD.1-TD.8）|
