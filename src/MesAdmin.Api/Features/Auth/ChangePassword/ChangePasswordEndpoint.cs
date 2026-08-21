using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace MesAdmin.Api.Features.Auth.ChangePassword;

/// <summary>
/// 自助修改密码：验证旧密码 → 设置新密码（≥8位且含字母和数字）。
/// 显式写审计 user.change-password（密码本身永不入库）。
/// </summary>
public class ChangePasswordEndpoint : Endpoint<ChangePasswordRequest>
{
    public required MesDbContext Db { get; set; }
    public required Pbkdf2PasswordHasher Hasher { get; set; }

    public override void Configure()
    {
        Post("/api/auth/change-password");
        Roles(MesRoles.All);
        Summary(s => s.Summary = "自助修改密码");
    }

    public override async Task HandleAsync(ChangePasswordRequest req, CancellationToken ct)
    {
        // JWT UniqueName 存的是显示名；登录名须从 user_id claim（= UserAccount.Id）反查
        var user = Ulid.TryParse(User.FindFirst("user_id")?.Value, out var userId)
            ? await Db.UserAccounts.FirstOrDefaultAsync(u => u.Id == userId, ct)
            : null;

        if (user is null || !user.IsActive)
            throw new InvalidOperationException("当前账号不存在");

        if (!Hasher.Verify(req.OldPassword, user.PasswordHash))
            throw new ArgumentException("旧密码不正确");

        user.ResetPassword(Hasher.Hash(req.NewPassword), DateTimeOffset.UtcNow);

        Db.AuditLogs.Add(AuditLog.Create(
            user.Username, "user.change-password", "auth", string.Empty, 200,
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty));

        await Db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}

[MemoryPackable]
public partial record ChangePasswordRequest(string OldPassword, string NewPassword);

/// <summary>改密请求校验：新密码强度策略。</summary>
public class ChangePasswordValidator : Validator<ChangePasswordRequest>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.OldPassword).NotEmpty().WithMessage("旧密码必填");
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8)
            .Matches(@"[a-zA-Z]").WithMessage("新密码须包含字母")
            .Matches(@"\d").WithMessage("新密码须包含数字");
    }
}
