using FastEndpoints;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.ProductionOrders.GetById;

public class GetOrderByIdEndpoint : MesEndpointWithoutRequest<ProductionOrderDetailResponse>
{
    public override void Configure()
    {
        Get("/{orderId}");
        Group<ProductionOrderGroup>();
        Summary(s => s.Summary = "获取工单详情");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var orderIdStr = Route<string>("orderId")!;
        if (!Ulid.TryParse(orderIdStr, out var id))
        {
            AddError("orderId", "无效的工单 Id");
            ThrowIfAnyErrors();
        }

        var order = await new GetOrderByIdQuery(id).ExecuteAsync(ct);
        if (order is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        Response = OrderMapper.ToDetail(order);
        await SendDualAsync(ct);
    }
}
