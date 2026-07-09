using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Common;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.ProductionOrders;

/// <summary>分页查询工单列表（支持状态/工单号/产品编码/日期范围过滤）。</summary>
[MemoryPackable]
public sealed partial record ListOrdersQuery(
    OrderStatus? Status,
    int Page,
    int Size,
    string? OrderNumberContains = null,
    string? ProductCode = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null) : ICommand<PagedResult<ProductionOrder>>;

internal sealed class ListOrdersHandler(
    IProductionOrderRepository orders) : ICommandHandler<ListOrdersQuery, PagedResult<ProductionOrder>>
{
    public async Task<PagedResult<ProductionOrder>> ExecuteAsync(ListOrdersQuery query, CancellationToken ct)
    {
        var pageIndex = Math.Max(query.Page, 1) - 1;
        var pageSize = Math.Clamp(query.Size, 1, 100);
        var filter = new OrderListFilter(
            query.Status,
            query.OrderNumberContains,
            query.ProductCode,
            query.CreatedFrom,
            query.CreatedTo);

        var total = await orders.CountAsync(filter, ct);
        var items = await orders.GetPageAsync(filter, pageIndex * pageSize, pageSize, ct);
        return new PagedResult<ProductionOrder>(items, total);
    }
}
