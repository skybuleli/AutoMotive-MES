using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace MesAdmin.Api.Features.Auth.Login;

/// <summary>
/// 用户登录（数据库账号版）。
/// 验证 UserAccount 凭据 → 失败计数/锁定 → 签发 JWT。
/// 登录事件（成功/失败/锁定）显式写入审计日志。
/// </summary>
public class LoginEndpoint : Endpoint<LoginRequest, LoginResponse>
{
    public required MesDbContext Db { get; set; }
    public required Pbkdf2PasswordHasher Hasher { get; set; }
    public required ITokenService TokenService { get; set; }

    public override void Configure()
    {
        Post("/api/auth/login");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "用户登录";
            s.Description = "验证凭据并签发 JWT；连续失败 5 次锁定 10 分钟";
        });
    }

    public override async Task HandleAsync(LoginRequest req, CancellationToken ct)
    {
        var username = req.Username.Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;

        var user = await Db.UserAccounts.FirstOrDefaultAsync(u => u.Username == username, ct);

        // 统一 401：不区分「不存在」与「密码错」，避免账号枚举
        if (user is null || !user.IsActive)
        {
            await WriteAuditAsync(username, "login.failed", $"账号不存在或已停用", 401, ct);
            await Send.UnauthorizedAsync(ct);
            return;
        }

        if (user.IsLockedOut)
        {
            await WriteAuditAsync(username, "login.locked",
                $"锁定期内拒绝登录，剩余 {(int)(user.LockoutUntil!.Value - now).TotalSeconds}s", 423, ct);
            await Send.StringAsync(string.Empty, StatusCodes.Status423Locked, cancellation: ct);
            return;
        }

        if (!Hasher.Verify(req.Password, user.PasswordHash))
        {
            var lockedNow = user.RegisterLoginFailure(now);
            await Db.SaveChangesAsync(ct);
            await WriteAuditAsync(username,
                lockedNow ? "login.locked" : "login.failed",
                lockedNow ? $"连续失败 {UserAccount.MaxFailedLoginAttempts} 次，触发锁定" : "密码错误",
                lockedNow ? StatusCodes.Status423Locked : 401, ct);
            await Send.UnauthorizedAsync(ct);
            return;
        }

        // 透明重哈希：旧迭代次数的哈希在验证通过后升级到当前强度
        if (Hasher.NeedsRehash(user.PasswordHash))
            user.ResetPassword(Hasher.Hash(req.Password), now);

        user.RegisterLoginSuccess(now);
        await Db.SaveChangesAsync(ct);

        await WriteAuditAsync(username, "login.success", string.Empty, 200, ct);

        var token = TokenService.GenerateToken(user.Id.ToString(), user.DisplayName, user.Roles);
        Response = new LoginResponse(token, user.DisplayName, user.Roles);
        await Send.OkAsync(Response, ct);
    }

    private async Task WriteAuditAsync(
        string username, string action, string detail, int statusCode, CancellationToken ct)
    {
        Db.AuditLogs.Add(AuditLog.Create(
            username, action, "auth", detail, statusCode,
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty));
        await Db.SaveChangesAsync(ct);
    }
}

[MemoryPackable]
public partial record LoginRequest(string Username, string Password);

[MemoryPackable]
public partial record LoginResponse(string Token, string User, string[] Roles);
