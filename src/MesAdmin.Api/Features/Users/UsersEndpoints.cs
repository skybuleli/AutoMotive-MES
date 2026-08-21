using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace MesAdmin.Api.Features.Users;

// ═══════════════════════════════════════════
//  用户管理 CRUD（IATF 演示级）
//  写操作由全局 AuditPostProcessor 自动审计（/api/auth/* 除外，auth 模块显式记事件）
// ═══════════════════════════════════════════

/// <summary>用户响应 DTO。</summary>
[MemoryPackable]
public partial record UserResponse(
    string Id,
    string Username,
    string DisplayName,
    string[] Roles,
    bool IsActive,
    DateTimeOffset? LastLoginAt,
    bool IsLockedOut,
    DateTimeOffset CreatedAt);

/// <summary>GET /api/v1/users — 分页列表（用户名/显示名模糊搜索）。</summary>
public class ListUsersEndpoint : MesEndpointWithoutRequest<List<UserResponse>>
{
    public required MesDbContext Db { get; set; }

    public override void Configure()
    {
        Get("/");
        Group<UsersGroup>();
        Summary(s => s.Summary = "查询用户列表");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var keyword = Query<string?>("keyword", isRequired: false)?.Trim();
        var pageIndex = Math.Max(0, Query<int?>("pageIndex", isRequired: false) ?? 0);
        var pageSize = Math.Clamp(Query<int?>("pageSize", isRequired: false) ?? 50, 1, 200);

        var query = Db.UserAccounts.AsNoTracking();
        if (!string.IsNullOrEmpty(keyword))
            query = query.Where(u => u.Username.Contains(keyword) || u.DisplayName.Contains(keyword));

        var users = await query
            .OrderBy(u => u.Username)
            .Skip(pageIndex * pageSize).Take(pageSize)
            .Select(u => new UserResponse(
                u.Id.ToString(), u.Username, u.DisplayName, u.Roles, u.IsActive,
                u.LastLoginAt, u.LockoutUntil > DateTimeOffset.UtcNow, u.CreatedAt))
            .ToListAsync(ct);

        Response = users;
        await SendDualAsync(ct);
    }
}

/// <summary>POST /api/v1/users — 新建用户。</summary>
public class CreateUserEndpoint : MesEndpoint<CreateUserRequest, UserResponse>
{
    public required MesDbContext Db { get; set; }
    public required Pbkdf2PasswordHasher Hasher { get; set; }

    public override void Configure()
    {
        Post("/");
        Group<UsersGroup>();
        Summary(s => s.Summary = "新建用户账号");
    }

    public override async Task HandleAsync(CreateUserRequest req, CancellationToken ct)
    {
        var username = req.Username.Trim().ToLowerInvariant();
        if (await Db.UserAccounts.AnyAsync(u => u.Username == username, ct))
            throw new InvalidOperationException($"用户名 {username} 已存在");

        var user = UserAccount.Create(
            Ulid.NewUlid(), username, req.DisplayName, Hasher.Hash(req.Password), req.Roles);
        Db.UserAccounts.Add(user);
        await Db.SaveChangesAsync(ct);

        Response = UserMapper.ToResponse(user);
        await SendCreatedDualAsync<ListUsersEndpoint>(new { }, ct);
    }
}

[MemoryPackable]
public partial class CreateUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string[] Roles { get; set; } = [];
}

/// <summary>PUT /api/v1/users/{id} — 编辑显示名/角色/启停用。</summary>
public class UpdateUserEndpoint : MesEndpoint<UpdateUserRequest, UserResponse>
{
    public required MesDbContext Db { get; set; }

    public override void Configure()
    {
        Put("/{id}");
        Group<UsersGroup>();
        Summary(s => s.Summary = "编辑用户（显示名/角色/启停用）");
    }

    public override async Task HandleAsync(UpdateUserRequest req, CancellationToken ct)
    {
        if (!Ulid.TryParse(Route<string>("id"), out var id))
            throw new ArgumentException("无效的用户 Id");

        var user = await Db.UserAccounts.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new KeyNotFoundException("用户不存在");

        // 防自锁：不能停用自己（否则唯一管理员可能把自己挡在系统外）
        var currentUserId = User.FindFirst("user_id")?.Value;
        if (!req.IsActive && !req.Roles.Contains(MesRoles.ProductionManager)
            && user.Id.ToString() == currentUserId)
            throw new InvalidOperationException("不能停用当前登录的管理员账号");

        user.DisplayName = req.DisplayName.Trim();
        user.Roles = req.Roles;
        user.IsActive = req.IsActive;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await Db.SaveChangesAsync(ct);

        Response = UserMapper.ToResponse(user);
        await SendDualAsync(ct);
    }
}

[MemoryPackable]
public partial class UpdateUserRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string[] Roles { get; set; } = [];
    public bool IsActive { get; set; } = true;
}

/// <summary>PUT /api/v1/users/{id}/reset-password — 管理员重置密码。</summary>
public class ResetPasswordEndpoint : MesEndpoint<ResetPasswordRequest, EmptyResponse>
{
    public required MesDbContext Db { get; set; }
    public required Pbkdf2PasswordHasher Hasher { get; set; }

    public override void Configure()
    {
        Put("/{id}/reset-password");
        Group<UsersGroup>();
        Summary(s => s.Summary = "重置用户密码");
    }

    public override async Task HandleAsync(ResetPasswordRequest req, CancellationToken ct)
    {
        if (!Ulid.TryParse(Route<string>("id"), out var id))
            throw new ArgumentException("无效的用户 Id");

        var user = await Db.UserAccounts.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new KeyNotFoundException("用户不存在");

        user.ResetPassword(Hasher.Hash(req.NewPassword), DateTimeOffset.UtcNow);
        await Db.SaveChangesAsync(ct);

        await SendDualAsync(ct);
    }
}

[MemoryPackable]
public partial class ResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}

// ── 校验器 ──

public class CreateUserValidator : Validator<CreateUserRequest>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(64)
            .Matches("^[a-z0-9_-]+$").WithMessage("用户名仅限小写字母/数字/下划线/连字符");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Password).ApplyPasswordRules();
        RuleFor(x => x.Roles).NotEmpty().WithMessage("至少分配一个角色")
            .Must(r => r.All(role => MesRoles.All.Contains(role)))
            .WithMessage("包含未定义的角色");
    }
}

public class UpdateUserValidator : Validator<UpdateUserRequest>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Roles).NotEmpty().WithMessage("至少分配一个角色")
            .Must(r => r.All(role => MesRoles.All.Contains(role)))
            .WithMessage("包含未定义的角色");
    }
}

public class ResetPasswordValidator : Validator<ResetPasswordRequest>
{
    public ResetPasswordValidator()
        => RuleFor(x => x.NewPassword).ApplyPasswordRules();
}

internal static class UserValidationExtensions
{
    /// <summary>密码策略：≥8 位且同时含字母与数字（与自助改密一致）。</summary>
    public static IRuleBuilderOptions<T, string> ApplyPasswordRules<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty().MinimumLength(8)
            .Matches(@"[a-zA-Z]").WithMessage("密码须包含字母")
            .Matches(@"\d").WithMessage("密码须包含数字");
}

/// <summary>实体 → 响应 DTO 映射。</summary>
internal static class UserMapper
{
    public static UserResponse ToResponse(UserAccount u) => new(
        u.Id.ToString(), u.Username, u.DisplayName, u.Roles, u.IsActive,
        u.LastLoginAt, u.IsLockedOut, u.CreatedAt);
}
