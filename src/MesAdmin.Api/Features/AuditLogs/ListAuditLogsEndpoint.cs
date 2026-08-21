using FastEndpoints;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MesAdmin.Api.Features.AuditLogs;

/// <summary>
/// GET /api/v1/audit-logs — 审计日志分页查询（只读，不可删改）。
/// 筛选：时间范围 / 用户名 / 模块；默认时间倒序。
/// </summary>
public class ListAuditLogsEndpoint : MesEndpointWithoutRequest<AuditLogPageResponse>
{
    public required MesDbContext Db { get; set; }

    public override void Configure()
    {
        Get("/api/v1/audit-logs");
        Roles(MesRoles.ProductionManager);
        Summary(s => s.Summary = "查询操作审计日志（IATF 追溯）");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var pageIndex = Math.Max(0, Query<int?>("pageIndex", isRequired: false) ?? 0);
        var pageSize = Math.Clamp(Query<int?>("pageSize", isRequired: false) ?? 50, 1, 200);
        var username = Query<string?>("username", isRequired: false)?.Trim();
        var module = Query<string?>("module", isRequired: false)?.Trim();
        var timeFrom = Query<DateTimeOffset?>("timeFrom", isRequired: false);
        var timeTo = Query<DateTimeOffset?>("timeTo", isRequired: false);

        var query = Db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrEmpty(username))
            query = query.Where(a => a.Username.Contains(username));
        if (!string.IsNullOrEmpty(module))
            query = query.Where(a => a.Module == module);
        if (timeFrom.HasValue)
            query = query.Where(a => a.Timestamp >= timeFrom.Value);
        if (timeTo.HasValue)
            query = query.Where(a => a.Timestamp <= timeTo.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip(pageIndex * pageSize).Take(pageSize)
            .Select(a => new AuditLogItem(
                a.Id.ToString(), a.Timestamp, a.Username, a.Action,
                a.Module, a.Summary, a.StatusCode, a.RemoteIp))
            .ToListAsync(ct);

        Response = new AuditLogPageResponse(total, pageIndex, pageSize, items);
        await SendDualAsync(ct);
    }
}

[MemoryPackable]
public partial record AuditLogItem(
    string Id,
    DateTimeOffset Timestamp,
    string Username,
    string Action,
    string Module,
    string Summary,
    int StatusCode,
    string RemoteIp);

[MemoryPackable]
public partial record AuditLogPageResponse(
    int Total,
    int PageIndex,
    int PageSize,
    IReadOnlyList<AuditLogItem> Items);
