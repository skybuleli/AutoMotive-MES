using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.ProductionOrders;

/// <summary>关闭工单命令。</summary>
[MemoryPackable]
public sealed partial record CloseOrderCommand(Ulid OrderId) : IWriteCommand<ProductionOrder>;

internal sealed class CloseOrderHandler(
    IProductionOrderRepository orders,
    ISapOrderSyncRecordRepository sapOrderSyncRepo) : ICommandHandler<CloseOrderCommand, ProductionOrder>
{
    public async Task<ProductionOrder> ExecuteAsync(CloseOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetByIdTrackedAsync(cmd.OrderId, ct)
            ?? throw new KeyNotFoundException($"工单 {cmd.OrderId} 不存在");

        order.Close();
        await orders.SaveChangesAsync(ct);

        // T3.14: 若工单来自 SAP，创建关闭状态同步记录
        if (!string.IsNullOrWhiteSpace(order.ExternalOrderNumber))
        {
            var syncRecord = SapOrderSyncRecord.Create(
                order.Id, order.OrderNumber, order.ExternalOrderNumber,
                OrderStatus.Closed);
            await sapOrderSyncRepo.AddAsync(syncRecord, ct);
            await sapOrderSyncRepo.SaveChangesAsync(ct);
        }

        return order;
    }
}
