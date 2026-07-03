using FastEndpoints;
using Microsoft.AspNetCore.Http;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.ProductionOrders.List;

public class ListOrdersEndpoint : MesEndpointWithoutRequest<List<ProductionOrderSummaryResponse>>
{
    public override void Configure()
    {
        Get("/");
        Group<ProductionOrderGroup>();
        Summary(s => s.Summary = "工单列表（分页）");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var status = Query<string?>("status", isRequired: false);
        var page = Query<int?>("page", isRequired: false) ?? 1;
        var size = Query<int?>("size", isRequired: false) ?? 20;

        OrderStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (Enum.TryParse<OrderStatus>(status, true, out var parsed))
                filter = parsed;
        }

        var result = await new ListOrdersQuery(filter, page, size).ExecuteAsync(ct);

        HttpContext.Response.Headers["X-Total-Count"] = result.Total.ToString();
        Response = result.Items.Select(OrderMapper.ToSummary).ToList();
        await SendDualAsync(ct);
    }
}
