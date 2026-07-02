using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.ProductionOrders;

/// <summary>完工命令（提交合格/不良数量）。</summary>
[MemoryPackable]
public sealed partial record CompleteOrderCommand(
    Ulid OrderId,
    int QualifiedQuantity,
    int DefectiveQuantity) : IWriteCommand<ProductionOrder>;

internal sealed class CompleteOrderHandler(
    IProductionOrderRepository orders) : ICommandHandler<CompleteOrderCommand, ProductionOrder>
{
    public async Task<ProductionOrder> ExecuteAsync(CompleteOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetByIdTrackedAsync(cmd.OrderId, ct)
            ?? throw new KeyNotFoundException($"工单 {cmd.OrderId} 不存在");

        order.Complete(cmd.QualifiedQuantity, cmd.DefectiveQuantity, DateTimeOffset.UtcNow);
        await orders.SaveChangesAsync(ct);
        return order;
    }
}
