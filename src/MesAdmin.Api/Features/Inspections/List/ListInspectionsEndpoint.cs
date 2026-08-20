using FastEndpoints;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Inspections.List;

/// <summary>
/// GET /api/v1/orders/{orderId}/inspections — 查询工单首件检验列表（按创建时间倒序）。
/// </summary>
public class ListInspectionsEndpoint : MesEndpointWithoutRequest<List<InspectionResponse>>
{
    public override void Configure()
    {
        Get("/");
        Group<InspectionGroup>();
        Summary(s => s.Summary = "查询工单首件检验列表");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var orderIdStr = Route<string>("orderId")!;
        if (!Ulid.TryParse(orderIdStr, out var orderId))
        {
            AddError("orderId", "无效的工单 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IFirstArticleInspectionRepository>();
        var inspections = await repo.GetByOrderIdAsync(orderId, ct);
        Response = inspections.Select(InspectionMapper.ToResponse).ToList();
        await SendDualAsync(ct);
    }
}
