using FastEndpoints;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.ProductionOrders.Operations.GetOperations;

public class GetOperationsEndpoint : MesEndpointWithoutRequest<List<OperationResponse>>
{
    public override void Configure()
    {
        Get("/{orderId}/operations");
        Group<ProductionOrderGroup>();
        Summary(s => s.Summary = "获取工单所有工序（31 工序）");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var orderIdStr = Route<string>("orderId")!;
        if (!Ulid.TryParse(orderIdStr, out var id))
        {
            AddError("orderId", "无效的工单 Id");
            ThrowIfAnyErrors();
        }

        var ops = await new GetOperationsQuery(id).ExecuteAsync(ct);
        Response = ops.Select(OrderMapper.ToOperationResponse).ToList();
        await SendDualAsync(ct);
    }
}
