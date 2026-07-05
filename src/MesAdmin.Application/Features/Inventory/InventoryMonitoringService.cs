using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Application.Features.Inventory;

/// <summary>
/// T1.13 线边库存实时监控服务。
/// 周期性扫描所有启用阈值的物料，比较当前可用库存 vs 阈值，
/// 触发黄色预警 / 红色报警 + 自动创建 JIT 拉动信号。
/// </summary>
public sealed class InventoryMonitoringService(
    IServiceScopeFactory scopeFactory,
    ILogger<InventoryMonitoringService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.ZLogInformation($"线边库存监控服务启动：每 {CheckInterval.TotalSeconds}s 扫描一次");

        // 首次延迟 10s 启动，等待系统稳定
        await Task.Delay(TimeSpan.FromSeconds(10), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CheckInventoryLevelsAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"线边库存监控扫描异常");
            }

            await Task.Delay(CheckInterval, ct);
        }

        logger.ZLogInformation($"线边库存监控服务已停止");
    }

    private async Task CheckInventoryLevelsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<IMaterialInventorySettingRepository>();
        var batchRepo = scope.ServiceProvider.GetRequiredService<IMaterialBatchRepository>();
        var alertRepo = scope.ServiceProvider.GetRequiredService<IInventoryAlertRepository>();
        var jitRepo = scope.ServiceProvider.GetRequiredService<IJitPullSignalRepository>();

        // 1. 获取所有启用的监控设置
        var settings = await settingsRepo.GetAllEnabledAsync(ct);
        if (settings.Count == 0)
            return;

        // 2. 批量查询当前库存
        var materialCodes = settings.Select(s => s.MaterialCode).Distinct().ToList();
        var quantities = await batchRepo.GetAvailableQuantitiesAsync(materialCodes, ct);

        // 3. 逐物料检查
        foreach (var setting in settings)
        {
            var currentQty = quantities.GetValueOrDefault(setting.MaterialCode, 0);

            // 查上一次生成的预警，避免重复预警
            var latestAlert = await alertRepo.GetLatestByMaterialAsync(setting.MaterialCode, ct);

            var alertLevel = setting.GetAlertLevel(currentQty);

            // 只有预警级别发生变化或预警未处理时才生成新预警
            if (alertLevel == InventoryAlertLevel.Normal)
            {
                if (latestAlert is not null && !latestAlert.IsResolved)
                {
                    logger.ZLogInformation(
                        $"库存恢复正常：{setting.MaterialCode} {setting.MaterialName} 当前量 {currentQty:F0} 高于安全库存 {setting.SafetyStock:F0}");
                }
                continue;
            }

            // 已有同级别未处理的预警 → 跳过
            if (latestAlert is not null && !latestAlert.IsResolved && latestAlert.AlertLevel == alertLevel)
                continue;

            // 4. 创建预警记录
            Ulid? jitSignalId = null;

            // 红色报警 → 自动创建 JIT 拉动信号
            if (alertLevel == InventoryAlertLevel.Red)
            {
                var shortage = setting.MinimumStock - currentQty;
                if (shortage > 0)
                {
                    var jitSignal = JitPullSignal.Create(
                        Ulid.Empty,
                        $"SYS-INVENTORY-{DateTimeOffset.UtcNow:yyyyMMdd}",
                        setting.MaterialCode,
                        setting.MaterialName,
                        Math.Round(shortage + setting.SafetyStock, 2),
                        setting.Unit,
                        setting.StationId ?? "线边仓");

                    await jitRepo.AddAsync(jitSignal, ct);
                    await jitRepo.SaveChangesAsync(ct);
                    jitSignalId = jitSignal.Id;

                    logger.ZLogWarning(
                        $"红色报警·自动叫料：{setting.MaterialCode} {setting.MaterialName} 当前 {currentQty:F0} < 最低 {setting.MinimumStock:F0}，已推送 JIT 拉动信号 {jitSignal.Id}");
                }
            }
            else
            {
                logger.ZLogWarning(
                    $"黄色预警：{setting.MaterialCode} {setting.MaterialName} 当前 {currentQty:F0} < 安全库存 {setting.SafetyStock:F0}");
            }

            var alert = InventoryAlert.Create(
                setting.MaterialCode,
                setting.MaterialName,
                currentQty,
                setting.SafetyStock,
                setting.MinimumStock,
                alertLevel,
                setting.StationId,
                jitSignalId);

            await alertRepo.AddAsync(alert, ct);
            await alertRepo.SaveChangesAsync(ct);
        }
    }
}
