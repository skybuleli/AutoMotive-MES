using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.ProductionOrders;

/// <summary>查询单个工单详情。</summary>
[MemoryPackable]
public sealed partial record GetOrderByIdQuery(Ulid OrderId) : ICommand<ProductionOrder?>;

internal sealed class GetOrderByIdHandler(
    IProductionOrderRepository orders) : ICommandHandler<GetOrderByIdQuery, ProductionOrder?>
{
    public Task<ProductionOrder?> ExecuteAsync(GetOrderByIdQuery query, CancellationToken ct)
        => orders.GetByIdAsync(query.OrderId, ct);
}
