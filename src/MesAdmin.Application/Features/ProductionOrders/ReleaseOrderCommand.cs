using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.ProductionOrders;

/// <summary>放行（下达）生产工单命令。</summary>
[MemoryPackable]
public sealed partial record ReleaseOrderCommand(Ulid OrderId) : IWriteCommand<ProductionOrder>;

internal sealed class ReleaseOrderHandler(
    IProductionOrderRepository orders) : ICommandHandler<ReleaseOrderCommand, ProductionOrder>
{
    public async Task<ProductionOrder> ExecuteAsync(ReleaseOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetByIdTrackedAsync(cmd.OrderId, ct)
            ?? throw new KeyNotFoundException($"工单 {cmd.OrderId} 不存在");

        order.Release();
        await orders.SaveChangesAsync(ct);
        return order;
    }
}
