using FastEndpoints;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.Dashboard;
using MesAdmin.Application.Security;

namespace MesAdmin.Api.Features.Dashboard.Summary;

public class DashboardSummaryEndpoint : MesEndpointWithoutRequest<DashboardSummaryResponse>
{
    public override void Configure()
    {
        Get("/api/v1/dashboard/summary");
        Roles(MesRoles.ProductionManager, MesRoles.ShiftLeader, MesRoles.WarehouseClerk, MesRoles.QualityEngineer, MesRoles.EquipmentEngineer);
        Summary(s => s.Summary = "生产看板数据聚合（工单计数+预警计数）");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var summary = await new DashboardSummaryQuery().ExecuteAsync(ct);
        Response = new DashboardSummaryResponse(
            summary.CreatedCount,
            summary.ReleasedCount,
            summary.InProgressCount,
            summary.CompletedCount,
            summary.ClosedCount,
            summary.ActiveAlerts,
            summary.RedAlerts,
            summary.YellowAlerts,
            summary.TodayQualified,
            summary.TodayDefective);
        await SendDualAsync(ct);
    }
}

/// <summary>看板数据聚合响应。</summary>
[MemoryPack.MemoryPackable]
public partial record DashboardSummaryResponse(
    int CreatedCount,
    int ReleasedCount,
    int InProgressCount,
    int CompletedCount,
    int ClosedCount,
    int ActiveAlerts,
    int RedAlerts,
    int YellowAlerts,
    int TodayQualified,
    int TodayDefective);
