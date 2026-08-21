# 审计日志 + 用户体系 设计文档

> 日期：2026-08-21
> 状态：已评审通过
> 目标水准：IATF 16949 审核演示（可追溯性达标，不做细粒度权限）

## 背景与问题

- 登录用户硬编码于 `LoginEndpoint.cs`（6 个演示账号，密码任意），无用户表
- 全系统无操作审计，不满足 IATF「谁在何时改了什么」追溯要求
- JWT Secret 明文位于 appsettings.json（本次不动，部署项）

## 决策记录

| 决策点 | 选择 | 否决方案 |
|--------|------|---------|
| 审计采集层 | FastEndpoints 全局 PostProcessor（覆盖所有端点） | EF SaveChanges 字段级 diff（噪音大）；业务代码显式调用（易漏） |
| 密码哈希 | PBKDF2-SHA256 100k 迭代 | BCrypt（无 .NET 内置依赖优势） |
| 种子数据落点 | DbInitializer 启动逻辑 | Migration 写死（不可变，改策略需新迁移） |
| 审计写入方式 | 直接 await 插库 | Channel 缓冲（管理端写操作低频，直写换零丢失） |
| 用户删除 | 不做物理删除，仅停用 | 物理删除（审计 Username 冗余快照已解耦，但演示级不需要） |

## 1. 数据模型

### UserAccount（Domain/Models，[MemoryPackable]，Ulid 主键）

```
Id / Username(唯一索引) / DisplayName / PasswordHash
Roles(string[], JSONB) / IsActive
FailedLoginCount / LockoutUntil? / LastLoginAt?
CreatedAt / UpdatedAt
```

### AuditLog

```
Id(Ulid) / Timestamp / Username(冗余快照，不做FK)
Action("POST /api/v1/orders/{id}/complete" 格式) / Module(路由第二段)
EntityId? / Summary(请求摘要≤500字，password字段打码)
StatusCode / RemoteIp
索引：(Timestamp DESC)、(Username)、(Module, Action)
```

Migration：`20260821_AddUsersAndAuditLog`

## 2. 认证改造

**Pbkdf2PasswordHasher**（Infrastructure/Security）：
存储格式 `pbkdf2-sha256$100000$<base64盐>$<base64哈希>`，算法标识入格式串，
未来迭代升级时旧哈希可验证并透明重哈希。

**登录流程**：

```
查 UserAccount → 不存在/停用 → 401（统一报错不泄露账号存在性）
→ LockoutUntil > now → 423
→ 密码错 → FailedLoginCount+1；≥5 → 锁定10分钟（记审计）
→ 成功 → 清零、更新LastLoginAt、签发JWT（现有流程不变）
```

Web 端 AuthService 与 JWT cookie 流程零改动。

**新增端点**：`POST /api/auth/change-password`（验旧密码；新密码≥8位含字母数字）。

**种子**：DbInitializer 库空时种入 6 演示账号，初始密码 `Mes@2026`。

## 3. 用户管理 API + 页面

| 端点 | 权限 | 说明 |
|------|------|------|
| GET /api/users?pageIndex&pageSize&keyword | ProductionManager | 分页+搜索 |
| POST /api/users | ProductionManager | 用户名唯一校验+初始密码 |
| PUT /api/users/{id} | ProductionManager | 显示名/角色/启停用（不可停用自己） |
| PUT /api/users/{id}/reset-password | ProductionManager | 重置密码 |

页面：`/users` MudTable 分页表格 + MudDialog 表单；顶栏菜单加自助改密对话框；
侧边栏新增「系统管理」分组。权限双层：页面 `[Authorize(Roles=...)]` + 端点校验。

## 4. 审计采集 + 查询页

**全局 PostProcessor**：非 GET 请求执行后记录——Username(JWT claims)、
Action(Method+路由模板)、Module、Summary(DTO序列化，password打码，截断500)、
StatusCode、RemoteIp。直接插库。

**显式事件**：login.success / login.failed / login.locked /
user.reset-password / user.change-password。密码永不入库。

**查询页 `/audit-logs`**：MudTable 服务端分页时间倒序，只读；
筛选：时间范围/用户/模块；权限 ProductionManager；挂「系统管理」组。

## 5. 测试与验收

单元测试：Pbkdf2PasswordHasherTests（往返/拒错/盐随机/格式兼容）、
LoginLockoutTests（5次锁定/窗口拒绝/过期解锁/成功清零）。

集成测试（Testcontainers）：种子登录→JWT角色正确；POST产生审计且密码打码、
GET不产生；用户CRUD+唯一冲突409；改密后旧密码失效。

浏览器验收：manager 见系统管理两页；新建用户可登录且审计可见；
错5次锁定且有 login.locked 记录；leader 无入口且直访被拒。

## 明确不做（YAGNI）

忘记密码邮件、密码过期、审计导出、按钮级权限、Refresh Token、细粒度权限矩阵。
