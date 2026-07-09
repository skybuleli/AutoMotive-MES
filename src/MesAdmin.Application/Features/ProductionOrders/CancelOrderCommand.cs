using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.ProductionOrders;

/// <summary>
/// 取消工单命令（生产开始前）。
/// 仅 Created/Released 状态允许，Saga 尚未启动，无需补偿。
/// </summary>
[MemoryPackable]
public sealed partial record CancelOrderCommand(
    Ulid OrderId,
    string Reason) : IWriteCommand<ProductionOrder>;

internal sealed class CancelOrderHandler(
    IProductionOrderRepository orders,
    ISapOrderSyncRecordRepository sapOrderSyncRepo) : ICommandHandler<CancelOrderCommand, ProductionOrder>
{
    public async Task<ProductionOrder> ExecuteAsync(CancelOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetByIdTrackedAsync(cmd.OrderId, ct)
            ?? throw new KeyNotFoundException($"工单 {cmd.OrderId} 不存在");

        order.Cancel(cmd.Reason, DateTimeOffset.UtcNow);
        await orders.SaveChangesAsync(ct);

        // 若工单来自 SAP，创建取消状态同步记录
        if (!string.IsNullOrWhiteSpace(order.ExternalOrderNumber))
        {
            var syncRecord = SapOrderSyncRecord.Create(
                order.Id, order.OrderNumber, order.ExternalOrderNumber,
                OrderStatus.Cancelled);
            await sapOrderSyncRepo.AddAsync(syncRecord, ct);
            await sapOrderSyncRepo.SaveChangesAsync(ct);
        }

        return order;
    }
}
