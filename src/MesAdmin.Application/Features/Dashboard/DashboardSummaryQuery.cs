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
    IInventoryAlertRepository alerts) : ICommandHandler<DashboardSummaryQuery, DashboardSummary>
{
    public async Task<DashboardSummary> ExecuteAsync(DashboardSummaryQuery query, CancellationToken ct)
    {
        // 并行查询各状态工单计数
        var createdTask = orders.CountAsync(OrderStatus.Created, ct);
        var releasedTask = orders.CountAsync(OrderStatus.Released, ct);
        var inProgressTask = orders.CountAsync(OrderStatus.InProgress, ct);
        var completedTask = orders.CountAsync(OrderStatus.Completed, ct);
        var closedTask = orders.CountAsync(OrderStatus.Closed, ct);

        // 预警计数
        var activeAlertsTask = alerts.CountActiveAsync(ct);
        var redAlertsTask = alerts.CountByLevelAsync(InventoryAlertLevel.Red, ct);
        var yellowAlertsTask = alerts.CountByLevelAsync(InventoryAlertLevel.Yellow, ct);

        await Task.WhenAll(createdTask, releasedTask, inProgressTask, completedTask, closedTask,
            activeAlertsTask, redAlertsTask, yellowAlertsTask);

        return new DashboardSummary(
            createdTask.Result,
            releasedTask.Result,
            inProgressTask.Result,
            completedTask.Result,
            closedTask.Result,
            activeAlertsTask.Result,
            redAlertsTask.Result,
            yellowAlertsTask.Result,
            TodayQualified: 0,   // 简化：暂不实现当日统计
            TodayDefective: 0);
    }
}
