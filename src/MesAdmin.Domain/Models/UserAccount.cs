using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 用户账号（审计日志+用户体系，IATF 演示级）。
/// 替代 LoginEndpoint 硬编码演示字典；密码 PBKDF2 哈希存储。
/// </summary>
[MemoryPackable]
public partial class UserAccount
{
    /// <summary>连续登录失败阈值：达到即锁定</summary>
    public const int MaxFailedLoginAttempts = 5;

    /// <summary>锁定时长</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(10);

    public Ulid Id { get; set; }

    /// <summary>登录名（唯一）</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>显示名（如 张经理）</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>PBKDF2 密码哈希（pbkdf2-sha256$迭代$盐$哈希 格式串）</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>角色列表（MesRoles 常量，JSONB 存储）</summary>
    public string[] Roles { get; set; } = [];

    /// <summary>是否启用（停用 = 禁止登录，不做物理删除以保审计连贯）</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>连续登录失败次数（成功后清零）</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>锁定截止时间（null/过去 = 未锁定）</summary>
    public DateTimeOffset? LockoutUntil { get; set; }

    /// <summary>最后成功登录时间</summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>当前是否处于锁定期</summary>
    [MemoryPackIgnore]
    public bool IsLockedOut => LockoutUntil.HasValue && LockoutUntil.Value > DateTimeOffset.UtcNow;

    public static UserAccount Create(
        Ulid id,
        string username,
        string displayName,
        string passwordHash,
        string[] roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentNullException.ThrowIfNull(roles);

        var now = DateTimeOffset.UtcNow;
        return new UserAccount
        {
            Id = id,
            Username = username.Trim().ToLowerInvariant(),
            DisplayName = displayName.Trim(),
            PasswordHash = passwordHash,
            Roles = roles,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>记录一次登录失败；达到阈值时触发锁定。返回本次是否新触发锁定。</summary>
    public bool RegisterLoginFailure(DateTimeOffset now)
    {
        FailedLoginCount++;
        UpdatedAt = now;

        if (FailedLoginCount < MaxFailedLoginAttempts) return false;

        LockoutUntil = now + LockoutDuration;
        FailedLoginCount = 0;
        return true;
    }

    /// <summary>记录成功登录：清零失败计数、解除锁定、刷新时间戳。</summary>
    public void RegisterLoginSuccess(DateTimeOffset now)
    {
        FailedLoginCount = 0;
        LockoutUntil = null;
        LastLoginAt = now;
        UpdatedAt = now;
    }

    /// <summary>重置密码哈希（管理员重置 / 自助改密共用）。</summary>
    public void ResetPassword(string newPasswordHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPasswordHash);
        PasswordHash = newPasswordHash;
        // 重置密码同时清除失败状态，避免改密后立即被旧失败计数锁定
        FailedLoginCount = 0;
        LockoutUntil = null;
        UpdatedAt = now;
    }
}

/// <summary>
/// 操作审计日志（IATF 16949 追溯要求：谁在何时对什么做了什么）。
/// 由 Api 全局 PostProcessor 自动采集写操作 + 登录端点显式事件。
/// 只增不改不删——不可变性是审核要点。
/// </summary>
[MemoryPackable]
public partial class AuditLog
{
    public Ulid Id { get; set; }

    /// <summary>发生时间（UTC）</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>操作人用户名（冗余快照而非外键——用户停用后历史仍完整可读）</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>动作标识："POST /api/v1/orders/{id}/complete" 或 "login.success"</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>业务模块（路由第二段：production/quality/maintenance/auth/users...）</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>请求摘要（DTO 序列化截断 500 字符，password 字段打码）</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>响应状态码（2xx=成功）</summary>
    public int StatusCode { get; set; }

    /// <summary>来源 IP</summary>
    public string RemoteIp { get; set; } = string.Empty;

    public static AuditLog Create(
        string username,
        string action,
        string module,
        string summary,
        int statusCode,
        string remoteIp)
    {
        return new AuditLog
        {
            Id = Ulid.NewUlid(),
            Timestamp = DateTimeOffset.UtcNow,
            Username = username,
            Action = action,
            Module = module,
            Summary = summary ?? string.Empty,
            StatusCode = statusCode,
            RemoteIp = remoteIp ?? string.Empty,
        };
    }
}
