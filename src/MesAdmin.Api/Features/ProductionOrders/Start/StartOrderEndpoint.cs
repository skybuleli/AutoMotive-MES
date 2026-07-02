using FastEndpoints;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.ProductionOrders.Start;

public class StartOrderEndpoint : MesEndpointWithoutRequest<ProductionOrderDetailResponse>
{
    public override void Configure()
    {
        Post("/{orderId}/start");
        Group<ProductionOrderGroup>();
        Roles(MesRoles.ShiftLeader, MesRoles.ProductionManager);
        Summary(s => s.Summary = "开工（触发 Saga）");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var orderIdStr = Route<string>("orderId")!;
        if (!Ulid.TryParse(orderIdStr, out var id))
        {
            AddError("orderId", "无效的工单 Id");
            ThrowIfAnyErrors();
        }

        var order = await new StartOrderCommand(id).ExecuteAsync(ct);
        Response = OrderMapper.ToDetail(order);
        await SendDualAsync(ct);
    }
}
