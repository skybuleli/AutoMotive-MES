using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.ProductionOrders;

/// <summary>
/// 完工确认命令（T1.8）。
/// 提交合格/不良数量，质量工程师审核放行，生成成品入库单 + 追溯标签码，
/// 并标记待同步 SAP 完工数量（实际 SAP 同步由 T3.14 后台作业执行）。
/// </summary>
[MemoryPackable]
public sealed partial record CompleteOrderCommand(
    Ulid OrderId,
    int QualifiedQuantity,
    int DefectiveQuantity,
    string ReviewerId) : IWriteCommand<ProductionOrder>;

internal sealed class CompleteOrderHandler(
    IProductionOrderRepository orders,
    IGoodsReceiptRepository goodsReceipts) : ICommandHandler<CompleteOrderCommand, ProductionOrder>
{
    public async Task<ProductionOrder> ExecuteAsync(CompleteOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetByIdTrackedAsync(cmd.OrderId, ct)
            ?? throw new KeyNotFoundException($"工单 {cmd.OrderId} 不存在");

        // 幂等：已存在入库单则跳过入库单创建（防止重复完工）
        var existingReceipt = await goodsReceipts.GetByOrderIdAsync(cmd.OrderId, ct);
        if (existingReceipt is not null)
            return order; // 已完工且已入库，幂等返回

        // 1. 状态机推进：InProgress → Completed（领域内校验数量上限）
        order.Complete(cmd.QualifiedQuantity, cmd.DefectiveQuantity, DateTimeOffset.UtcNow);
        await orders.SaveChangesAsync(ct);

        // 2. T1.8 质量审核放行 + 成品入库 + 追溯标签码生成
        //    追溯标签码编码规则在 GoodsReceipt.Create 内实现（ESP9-YYYYMMDD-NNNN）
        var receipt = GoodsReceipt.Create(
            order.Id,
            order.OrderNumber,
            order.ProductCode,
            cmd.QualifiedQuantity,
            cmd.ReviewerId,
            DateTimeOffset.UtcNow);
        await goodsReceipts.AddAsync(receipt, ct);
        await goodsReceipts.SaveChangesAsync(ct);

        // 3. SAP 完工数量同步：标记为待同步（SapSynced=false），
        //    实际 SAP RFC/BAPI 调用由 T3.14 后台作业轮询 SapSynced=false 的入库单执行。
        //    此处不直接调用 SAP，避免工单完工流程被外部系统可用性阻塞。

        return order;
    }
}
