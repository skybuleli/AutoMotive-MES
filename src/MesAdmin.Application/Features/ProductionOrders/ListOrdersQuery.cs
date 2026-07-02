using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Common;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.ProductionOrders;

/// <summary>分页查询工单列表。</summary>
[MemoryPackable]
public sealed partial record ListOrdersQuery(
    OrderStatus? Status,
    int Page,
    int Size) : ICommand<PagedResult<ProductionOrder>>;

internal sealed class ListOrdersHandler(
    IProductionOrderRepository orders) : ICommandHandler<ListOrdersQuery, PagedResult<ProductionOrder>>
{
    public async Task<PagedResult<ProductionOrder>> ExecuteAsync(ListOrdersQuery query, CancellationToken ct)
    {
        var pageIndex = Math.Max(query.Page, 1) - 1;
        var pageSize = Math.Clamp(query.Size, 1, 100);
        var total = await orders.CountAsync(query.Status, ct);
        var items = await orders.GetPageAsync(query.Status, pageIndex * pageSize, pageSize, ct);
        return new PagedResult<ProductionOrder>(items, total);
    }
}
