using FastEndpoints;
using FluentValidation;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Inventory.Monitoring;

public class InventoryMonitoringEndpoint : MesEndpointWithoutRequest<InventoryMonitoringResponse>
{
    public override void Configure()
    {
        Get("/inventory/monitoring");
        Group<InventoryGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.ShiftLeader, MesRoles.WarehouseClerk, MesRoles.QualityEngineer);
        Summary(s => s.Summary = "线边库存实时监控（当前库存 vs 阈值：绿色正常/黄色预警/红色报警）");
        Description(d => d
            .Produces<InventoryMonitoringResponse>(200)
            .ProducesProblem(401));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var settingsRepo = Resolve<IMaterialInventorySettingRepository>();
        var batchRepo = Resolve<IMaterialBatchRepository>();
        var alertRepo = Resolve<IInventoryAlertRepository>();

        // 1. 获取所有启用阈值的物料
        var settings = await settingsRepo.GetAllEnabledAsync(ct);

        // 2. 批量查询当前库存
        var materialCodes = settings.Select(s => s.MaterialCode).Distinct().ToList();
        var quantities = await batchRepo.GetAvailableQuantitiesAsync(materialCodes, ct);

        // 3. 获取所有未处理的预警
        var activeAlerts = await alertRepo.GetActiveAsync(ct);
        var alertKeys = activeAlerts
            .Select(a => (a.MaterialCode, StationId: a.StationId ?? ""))
            .ToHashSet();

        // 4. 构建响应
        var items = settings.Select(s =>
        {
            var currentQty = quantities.GetValueOrDefault(s.MaterialCode, 0);
            var alertLevel = s.GetAlertLevel(currentQty);
            var hasActiveAlert = alertKeys.Contains((s.MaterialCode, s.StationId ?? ""));

            return new InventoryItemStatus(
                s.MaterialCode,
                s.MaterialName,
                s.StationId ?? "通用",
                Math.Round(currentQty, 2),
                s.SafetyStock,
                s.MinimumStock,
                s.Unit,
                alertLevel.ToString(),
                hasActiveAlert,
                s.IsCritical);
        }).ToList();

        var totalAlerts = items.Count(i => i.AlertLevel != "Normal");
        var redCount = items.Count(i => i.AlertLevel == "Red");
        var yellowCount = items.Count(i => i.AlertLevel == "Yellow");

        Response = new InventoryMonitoringResponse(items, totalAlerts, redCount, yellowCount);
        await SendDualAsync(ct);
    }
}

public class InventoryGroup : Group
{
    public InventoryGroup() => Configure("api/v1/inventory", ep => { });
}

/// <summary>线边库存监控响应。</summary>
[MemoryPack.MemoryPackable]
public partial record InventoryMonitoringResponse(
    List<InventoryItemStatus> Items,
    int TotalAlerts,
    int RedCount,
    int YellowCount);

/// <summary>单项物料库存状态。</summary>
[MemoryPack.MemoryPackable]
public partial record InventoryItemStatus(
    string MaterialCode,
    string MaterialName,
    string StationId,
    double CurrentQuantity,
    double SafetyStock,
    double MinimumStock,
    string Unit,
    string AlertLevel,
    bool HasActiveAlert,
    bool IsCritical);
