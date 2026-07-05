using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Application.Features.ProductionOrders;

/// <summary>
/// T1.17 物料消耗反冲命令。
/// 工单完工后按 BOM 标准用量扣减线边库存、差异>2%生成异常报告、创建 SAP 同步记录。
/// </summary>
[MemoryPackable]
public sealed partial record BackflushMaterialsCommand(Ulid OrderId) : IWriteCommand<BackflushResult>;

/// <summary>反冲执行结果。</summary>
[MemoryPackable]
public sealed partial record BackflushResult(
    bool Success,
    int ConsumedCount,
    int VarianceCount,
    int SapSyncCount,
    List<string> Warnings);

public sealed class BackflushMaterialsHandler(
    IProductionOrderRepository orders,
    IBomRepository boms,
    IMaterialBatchRepository batches,
    IMaterialBindingRepository bindings,
    IMaterialConsumptionRepository consumptions,
    IConsumptionVarianceRepository variances,
    ISapInventorySyncRecordRepository sapSyncRecords,
    ILogger<BackflushMaterialsHandler> logger) : ICommandHandler<BackflushMaterialsCommand, BackflushResult>
{
    private const double VarianceThresholdPercent = 2.0;

    public async Task<BackflushResult> ExecuteAsync(BackflushMaterialsCommand cmd, CancellationToken ct)
    {
        // 1. 加载工单
        var order = await orders.GetByIdTrackedAsync(cmd.OrderId, ct)
            ?? throw new KeyNotFoundException($"工单 {cmd.OrderId} 不存在");

        if (order.Status != OrderStatus.Completed)
            throw new InvalidOperationException($"工单 {order.OrderNumber} 状态为 {order.Status}，仅 Completed 状态可执行反冲");

        // 幂等：已存在消耗记录则跳过
        var existingConsumptions = await consumptions.GetByOrderIdAsync(order.Id, ct);
        if (existingConsumptions.Count > 0)
        {
            logger.ZLogInformation($"反冲幂等跳过：工单 {order.OrderNumber} 已执行过消耗反冲");
            return new BackflushResult(true, existingConsumptions.Count, 0, 0, ["已执行过反冲，跳过"]);
        }

        // 2. 加载 BOM
        var bom = await boms.GetByProductAndVersionAsync(order.ProductCode, order.BomVersion, ct);
        if (bom is null || bom.Items.Count == 0)
        {
            logger.ZLogWarning($"反冲跳过：工单 {order.OrderNumber} 未找到 BOM {order.ProductCode}/{order.BomVersion}");
            return new BackflushResult(false, 0, 0, 0, [$"未找到 BOM {order.ProductCode}/{order.BomVersion}"]);
        }

        var qualifiedQty = order.QualifiedQuantity;
        var warnings = new List<string>();

        // 3. 获取该工单的所有投料绑定记录（按物料编码汇总实际用量）
        var bindingsList = await bindings.GetByOrderIdAsync(order.Id, ct);
        var actualBindingsByMaterial = bindingsList
            .GroupBy(b => b.MaterialCode)
            .ToDictionary(g => g.Key, g => g.Sum(b => b.Quantity));

        // 4. 获取所有 BOM 关键物料编码
        // 与 T1.4 一致：仅对关键物料做消耗反冲
        var bomItems = bom.Items.Where(i => i.IsCritical).ToList();

        // 5. 获取当前库存（MaterialBatch 按物料编码汇总）
        var materialCodes = bomItems.Select(i => i.MaterialCode).Distinct().ToList();
        var availableQtys = await batches.GetAvailableQuantitiesAsync(materialCodes, ct);

        var consumptionList = new List<MaterialConsumption>();
        var varianceList = new List<ConsumptionVarianceReport>();
        var sapSyncList = new List<SapInventorySyncRecord>();

        foreach (var item in bomItems)
        {
            // BOM 标准用量 = 合格数量 × 单台用量
            var standardQty = Math.Round(qualifiedQty * item.QuantityPerUnit, 2);
            if (standardQty <= 0) continue;

            // 实际投料绑定数量
            var actualBound = actualBindingsByMaterial.GetValueOrDefault(item.MaterialCode, 0);

            // 实际消耗数量：优先扣减已绑定的批次库存
            // 先从已绑定的批次中扣减，不足时从其他 Qualified 批次补足
            var consumedQty = await DeductFromBatchesAsync(
                item.MaterialCode, standardQty, bindingsList, ct);

            // 创建消耗记录
            var consumption = MaterialConsumption.Create(
                order.Id, order.OrderNumber,
                item.MaterialCode, item.MaterialName,
                standardQty, actualBound, consumedQty,
                item.Unit, item.IsCritical);
            consumptionList.Add(consumption);

            // 检查差异是否超过 2%
            if (consumption.IsVarianceExceeded)
            {
                var report = ConsumptionVarianceReport.Create(
                    order.Id, order.OrderNumber,
                    item.MaterialCode, item.MaterialName,
                    consumption.StandardQuantity,
                    consumption.ConsumedQuantity,
                    consumption.VarianceQuantity,
                    consumption.VariancePercent,
                    item.Unit);
                varianceList.Add(report);

                warnings.Add(
                    $"差异 {consumption.VariancePercent:F1}%：{item.MaterialCode} 标准 {standardQty} 实际消耗 {consumedQty} ({item.Unit})");
            }

            // 创建 SAP 同步记录（移动类型 261=工单发料）
            if (consumedQty > 0)
            {
                var sapRecord = SapInventorySyncRecord.Create(
                    order.Id, order.OrderNumber,
                    item.MaterialCode, consumedQty, item.Unit, "261");
                sapSyncList.Add(sapRecord);
            }
        }

        // 6. 批量写入
        if (consumptionList.Count > 0)
        {
            await consumptions.AddRangeAsync(consumptionList, ct);
        }
        foreach (var report in varianceList)
        {
            await variances.AddAsync(report, ct);
        }
        if (sapSyncList.Count > 0)
        {
            await sapSyncRecords.AddRangeAsync(sapSyncList, ct);
        }

        // SaveChangesAsync 一次提交所有更改（包括 batches 的库存扣减）
        await consumptions.SaveChangesAsync(ct);

        logger.ZLogInformation(
            $"物料反冲完成：工单 {order.OrderNumber} 消耗 {consumptionList.Count} 种物料，" +
            $"差异 {varianceList.Count} 项，SAP 待同步 {sapSyncList.Count} 条");

        return new BackflushResult(
            true,
            consumptionList.Count,
            varianceList.Count,
            sapSyncList.Count,
            warnings);
    }

    /// <summary>
    /// 按 BOM 标准用量从物料批次扣减库存。
    /// 优先扣减已绑定到该工单的批次，不足时从其他 Qualified 批次扣减。
    /// </summary>
    private async Task<double> DeductFromBatchesAsync(
        string materialCode, double requiredQty,
        List<MaterialBinding> bindings, CancellationToken ct)
    {
        var remaining = requiredQty;
        var totalConsumed = 0.0;

        // 先从已绑定到该工单的批次扣减
        var orderBindings = bindings
            .Where(b => b.MaterialCode == materialCode)
            .GroupBy(b => b.MaterialBatchId)
            .ToList();

        // 如果该物料没有被手动绑定过，直接从 Qualified 批次扣减
        if (orderBindings.Count == 0)
        {
            return await DeductFromQualifiedBatchesAsync(materialCode, requiredQty, ct);
        }

        // 先扣已绑定的批次
        foreach (var bindingGroup in orderBindings)
        {
            if (remaining <= 0) break;

            var batchId = bindingGroup.Key;
            var batch = await batches.GetByIdTrackedAsync(batchId, ct);
            if (batch is null || batch.Status != MaterialBatchStatus.Qualified) continue;

            var availableInBatch = Math.Min(batch.RemainingQuantity, remaining);
            if (availableInBatch > 0)
            {
                batch.Consume(availableInBatch);
                totalConsumed += availableInBatch;
                remaining -= availableInBatch;
            }
        }

        // 如果已绑定的批次不够，从其他 Qualified 批次补足
        if (remaining > 0)
        {
            totalConsumed += await DeductFromQualifiedBatchesAsync(materialCode, remaining, ct);
        }

        return totalConsumed;
    }

    /// <summary>从 Qualified 批次扣减库存（按 FIFO 顺序，EF 跟踪状态以持久化 Consume 调用）。</summary>
    private async Task<double> DeductFromQualifiedBatchesAsync(
        string materialCode, double requiredQty, CancellationToken ct)
    {
        var remaining = requiredQty;
        var totalConsumed = 0.0;

        // 直接获取跟踪状态的批次（避免 N+1：先 AsNoTracking 查询再逐条 GetByIdTrackedAsync）
        var trackedBatches = await batches.GetTrackedPageAsync(materialCode, 0, 100, ct);
        var qualifiedBatches = trackedBatches
            .Where(b => b.Status == MaterialBatchStatus.Qualified && b.RemainingQuantity > 0)
            .OrderBy(b => b.ProductionDate ?? b.ReceivedAt)
            .ToList();

        foreach (var batch in qualifiedBatches)
        {
            if (remaining <= 0) break;

            var availableInBatch = Math.Min(batch.RemainingQuantity, remaining);
            if (availableInBatch > 0)
            {
                batch.Consume(availableInBatch);
                totalConsumed += availableInBatch;
                remaining -= availableInBatch;
            }
        }

        return totalConsumed;
    }
}
