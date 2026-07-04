using FastEndpoints;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.ProductionOrders.Close;

public class CloseOrderEndpoint : MesEndpointWithoutRequest<ProductionOrderDetailResponse>
{
    public override void Configure()
    {
        Post("/{orderId}/close");
        Group<ProductionOrderGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.ShiftLeader, MesRoles.QualityEngineer);
        Summary(s => s.Summary = "关闭工单");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var orderIdStr = Route<string>("orderId")!;
        if (!Ulid.TryParse(orderIdStr, out var id))
        {
            AddError("orderId", "无效的工单 Id");
            ThrowIfAnyErrors();
        }

        var order = await new CloseOrderCommand(id).ExecuteAsync(ct);
        Response = OrderMapper.ToDetail(order);
        await SendDualAsync(ct);
    }
}
