using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.Dashboard;

/// <summary>
/// T1.11 生产看板数据聚合查询。
/// 返回工单各状态计数 + 库存预警计数 + 今日产量统计。
/// </summary>
[MemoryPackable]
public sealed partial record DashboardSummaryQuery : ICommand<DashboardSummary>;

[MemoryPackable]
public sealed partial record DashboardSummary(
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

internal sealed class DashboardSummaryHandler(
    IProductionOrderRepository orders,
    IInventoryAlertRepository alerts,
    IHydraulicTestRepository hydraulics) : ICommandHandler<DashboardSummaryQuery, DashboardSummary>
{
    public async Task<DashboardSummary> ExecuteAsync(DashboardSummaryQuery query, CancellationToken ct)
    {
        // 当日统计：以液压测试完成时间（每件产品必经站4）为口径，UTC 00:00 起算
        var today = DateTimeOffset.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var (todayQualified, todayDefective) = await hydraulics.CountByCompletedPeriodAsync(today, tomorrow, ct);

        return new DashboardSummary(
            await orders.CountAsync(OrderStatus.Created, ct),
            await orders.CountAsync(OrderStatus.Released, ct),
            await orders.CountAsync(OrderStatus.InProgress, ct),
            await orders.CountAsync(OrderStatus.Completed, ct),
            await orders.CountAsync(OrderStatus.Closed, ct),
            await alerts.CountActiveAsync(ct),
            await alerts.CountByLevelAsync(InventoryAlertLevel.Red, ct),
            await alerts.CountByLevelAsync(InventoryAlertLevel.Yellow, ct),
            TodayQualified: todayQualified,
            TodayDefective: todayDefective);
    }
}
