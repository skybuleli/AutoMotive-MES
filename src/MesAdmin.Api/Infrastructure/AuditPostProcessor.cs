using System.Text.RegularExpressions;
using FastEndpoints;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using ZLogger;

namespace MesAdmin.Api.Infrastructure;

/// <summary>
/// 全局审计后处理器：自动记录所有写操作（非 GET）到 audit_logs。
/// - 跳过 /api/auth/*（登录/改密由端点显式记语义化事件，避免密码体入库）
/// - 跳过 /health 等非业务路径
/// - 请求 DTO 序列化为摘要，password 字段打码，截断 500 字符
/// - Username 从 user_id claim 反查（JWT UniqueName 存的是显示名）
/// 写操作低频，直接插库换取零丢失。
/// </summary>
public sealed partial class AuditPostProcessor(ILogger<AuditPostProcessor> logger) : IGlobalPostProcessor
{
    private const int MaxSummaryLength = 500;

    /// <summary>字段名含 password 的 JSON 属性值打码。</summary>
    [GeneratedRegex(@"""(?<name>[^""]*password[^""]*)""\s*:\s*""(?<value>[^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PasswordMaskRegex();

    public async Task PostProcessAsync(IPostProcessorContext ctx, CancellationToken ct)
    {
        var http = ctx.HttpContext;
        var request = http.Request;

        if (HttpMethods.IsGet(request.Method)) return;
        if (request.Path.StartsWithSegments("/api/auth")) return;
        if (request.Path.StartsWithSegments("/health")) return;
        if (request.Path.StartsWithSegments("/internal")) return;

        try
        {
            var username = await ResolveUsernameAsync(http, ct);
            var module = ExtractModule(request.Path);
            var summary = BuildSummary(ctx);

            var db = http.RequestServices.GetRequiredService<MesDbContext>();
            db.AuditLogs.Add(AuditLog.Create(
                username,
                $"{request.Method} {request.Path}",
                module,
                summary,
                http.Response.StatusCode,
                http.Connection.RemoteIpAddress?.ToString() ?? string.Empty));

            // 审计插入独立提交；失败只记日志不影响响应（响应已开始发送时尤其如此）
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, $"审计日志写入失败：{request.Method} {request.Path}");
        }
    }

    private static async Task<string> ResolveUsernameAsync(HttpContext http, CancellationToken ct)
    {
        if (Ulid.TryParse(http.User.FindFirst("user_id")?.Value, out var userId))
        {
            var db = http.RequestServices.GetRequiredService<MesDbContext>();
            var found = await db.UserAccounts.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.Username)
                .FirstOrDefaultAsync(ct);
            if (found is not null) return found;
        }
        return http.User.Identity?.Name ?? "anonymous";
    }

    private static string ExtractModule(PathString path)
    {
        // /api/v1/{module}/... 或 /api/{module}/...
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        return segments.Length >= 3 ? segments[2] : (segments.Length >= 2 ? segments[1] : "unknown");
    }

    private static string BuildSummary(IPostProcessorContext ctx)
    {
        try
        {
            // FE 上下文直接暴露请求 DTO；无 DTO 的请求记空摘要
            var dto = ctx.Request;
            if (dto is null)
                return string.Empty;

            var json = System.Text.Json.JsonSerializer.Serialize(dto, dto.GetType());
            json = PasswordMaskRegex().Replace(json, $"\"${{name}}\":\"***\"");
            return json.Length <= MaxSummaryLength ? json : json[..MaxSummaryLength];
        }
        catch (Exception ex)
        {
            // 摘要序列化失败不阻塞审计主流程；记录诊断信息后返回空摘要
            System.Diagnostics.Debug.WriteLine($"BuildSummary failed: {ex}");
            return string.Empty;
        }
    }
}
