using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.ProductionOrders;

/// <summary>关闭工单命令。</summary>
[MemoryPackable]
public sealed partial record CloseOrderCommand(Ulid OrderId) : IWriteCommand<ProductionOrder>;

internal sealed class CloseOrderHandler(
    IProductionOrderRepository orders) : ICommandHandler<CloseOrderCommand, ProductionOrder>
{
    public async Task<ProductionOrder> ExecuteAsync(CloseOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(cmd.OrderId, ct)
            ?? throw new KeyNotFoundException($"工单 {cmd.OrderId} 不存在");

        order.Close();
        orders.Update(order);
        await orders.SaveChangesAsync(ct);
        return order;
    }
}
