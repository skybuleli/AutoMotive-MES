using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Application.Features.ProductionOrders;

/// <summary>
/// 完工确认命令（T1.8 + T1.17 物料消耗反冲触发）。
/// 提交合格/不良数量，质量工程师审核放行，生成成品入库单 + 追溯标签码，
/// 完工后自动触发 T1.17 物料消耗反冲（BOM 扣减库存 + 差异检查 + SAP 同步记录）。
/// </summary>
[MemoryPackable]
public sealed partial record CompleteOrderCommand(
    Ulid OrderId,
    int QualifiedQuantity,
    int DefectiveQuantity,
    string ReviewerId) : IWriteCommand<ProductionOrder>;

internal sealed class CompleteOrderHandler(
    IProductionOrderRepository orders,
    IGoodsReceiptRepository goodsReceipts,
    ISapOrderSyncRecordRepository sapOrderSyncRepo,
    ILogger<CompleteOrderHandler> logger,
    BackflushMaterialsHandler? backflushHandler = null) : ICommandHandler<CompleteOrderCommand, ProductionOrder>
{
    public async Task<ProductionOrder> ExecuteAsync(CompleteOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetByIdTrackedAsync(cmd.OrderId, ct)
            ?? throw new KeyNotFoundException($"工单 {cmd.OrderId} 不存在");

        // 幂等：已存在入库单则跳过入库单创建（防止重复完工）
        var existingReceipt = await goodsReceipts.GetByOrderIdAsync(cmd.OrderId, ct);
        if (existingReceipt is not null)
        {
            await RunBackflushAsync(cmd.OrderId, order.OrderNumber, ct);
            return order;
        }

        // 1. 状态机推进：InProgress → Completed
        order.Complete(cmd.QualifiedQuantity, cmd.DefectiveQuantity, DateTimeOffset.UtcNow);
        await orders.SaveChangesAsync(ct);

        // 2. T1.8 质量审核放行 + 成品入库 + 追溯标签码生成
        var receipt = GoodsReceipt.Create(
            order.Id, order.OrderNumber, order.ProductCode,
            cmd.QualifiedQuantity, cmd.ReviewerId, DateTimeOffset.UtcNow);
        await goodsReceipts.AddAsync(receipt, ct);
        await goodsReceipts.SaveChangesAsync(ct);

        // 3. T3.14: 若工单来自 SAP，创建工单状态同步记录
        if (!string.IsNullOrWhiteSpace(order.ExternalOrderNumber))
        {
            var syncRecord = SapOrderSyncRecord.Create(
                order.Id, order.OrderNumber, order.ExternalOrderNumber,
                OrderStatus.Completed, cmd.QualifiedQuantity);
            await sapOrderSyncRepo.AddAsync(syncRecord, ct);
        }

        await RunBackflushAsync(cmd.OrderId, order.OrderNumber, ct);

        return order;
    }

    private async Task RunBackflushAsync(Ulid orderId, string orderNumber, CancellationToken ct)
    {
        if (backflushHandler is null)
            return;

        var backflushResult = await backflushHandler.ExecuteAsync(new BackflushMaterialsCommand(orderId), ct);
        if (backflushResult.VarianceCount > 0)
        {
            logger.ZLogWarning(
                $"完工反冲完成：工单 {orderNumber} 有 {backflushResult.VarianceCount} 项差异超阈值，" +
                $"SAP 待同步 {backflushResult.SapSyncCount} 条");
        }
        else
        {
            logger.ZLogInformation(
                $"完工反冲完成：工单 {orderNumber} 消耗 {backflushResult.ConsumedCount} 种物料，无差异");
        }
    }
}
