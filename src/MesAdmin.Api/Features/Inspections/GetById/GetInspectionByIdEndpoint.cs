using FastEndpoints;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.Inspections;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Inspections.GetById;

public class GetInspectionByIdEndpoint : MesEndpointWithoutRequest<InspectionResponse>
{
    public override void Configure()
    {
        Get("/{inspectionId}");
        Group<InspectionGroup>();
        Summary(s => s.Summary = "获取首件检验详情");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var inspectionIdStr = Route<string>("inspectionId")!;
        if (!Ulid.TryParse(inspectionIdStr, out var inspectionId))
        {
            AddError("inspectionId", "无效的检验 Id");
            ThrowIfAnyErrors();
        }

        var inspection = await new GetInspectionByIdQuery(inspectionId).ExecuteAsync(ct);
        if (inspection is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        Response = InspectionMapper.ToResponse(inspection);
        await SendDualAsync(ct);
    }
}
