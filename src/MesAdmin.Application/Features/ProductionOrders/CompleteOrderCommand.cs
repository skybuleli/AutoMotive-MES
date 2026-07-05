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
    ILogger<CompleteOrderHandler> logger) : ICommandHandler<CompleteOrderCommand, ProductionOrder>
{
    public async Task<ProductionOrder> ExecuteAsync(CompleteOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetByIdTrackedAsync(cmd.OrderId, ct)
            ?? throw new KeyNotFoundException($"工单 {cmd.OrderId} 不存在");

        // 幂等：已存在入库单则跳过入库单创建（防止重复完工）
        var existingReceipt = await goodsReceipts.GetByOrderIdAsync(cmd.OrderId, ct);
        if (existingReceipt is not null)
            return order;

        // 1. 状态机推进：InProgress → Completed
        order.Complete(cmd.QualifiedQuantity, cmd.DefectiveQuantity, DateTimeOffset.UtcNow);
        await orders.SaveChangesAsync(ct);

        // 2. T1.8 质量审核放行 + 成品入库 + 追溯标签码生成
        var receipt = GoodsReceipt.Create(
            order.Id, order.OrderNumber, order.ProductCode,
            cmd.QualifiedQuantity, cmd.ReviewerId, DateTimeOffset.UtcNow);
        await goodsReceipts.AddAsync(receipt, ct);
        await goodsReceipts.SaveChangesAsync(ct);

        // 3. T1.17 物料消耗反冲（工单完工后自动触发）
        // 根据 BOM 标准用量扣减线边库存，差异 > 2% 生成异常报告，创建 SAP 同步记录
        try
        {
            var backflushResult = await new BackflushMaterialsCommand(cmd.OrderId).ExecuteAsync(ct);
            if (backflushResult.VarianceCount > 0)
            {
                logger.ZLogWarning(
                    $"完工反冲完成：工单 {order.OrderNumber} 有 {backflushResult.VarianceCount} 项差异超阈值，" +
                    $"SAP 待同步 {backflushResult.SapSyncCount} 条");
            }
            else
            {
                logger.ZLogInformation(
                    $"完工反冲完成：工单 {order.OrderNumber} 消耗 {backflushResult.ConsumedCount} 种物料，无差异");
            }
        }
        catch (Exception ex) when (ex is not KeyNotFoundException)
        {
            // 反冲失败不阻塞完工流程，记录日志供后续处理
            logger.ZLogWarning(ex, $"完工反冲异常（不阻塞完工）：工单 {order.OrderNumber}");
        }

        return order;
    }
}
