using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Security;
using MesAdmin.Application.Features.Inspections;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Inspections.RecordValue;

public class RecordValueEndpoint : MesEndpoint<RecordValueRequest, InspectionResponse>
{
    public override void Configure()
    {
        Patch("/{inspectionId}/items/{characteristicCode}");
        Group<InspectionGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "记录检验项实测值");
    }

    public override async Task HandleAsync(RecordValueRequest req, CancellationToken ct)
    {
        var inspectionIdStr = Route<string>("inspectionId")!;
        var characteristicCode = Route<string>("characteristicCode")!;

        if (!Ulid.TryParse(inspectionIdStr, out var inspectionId))
        {
            AddError("inspectionId", "无效的检验 Id");
            ThrowIfAnyErrors();
        }

        var inspection = await new RecordInspectionValueCommand(inspectionId, characteristicCode, req.ActualValue).ExecuteAsync(ct);
        Response = InspectionMapper.ToResponse(inspection);
        await SendDualAsync(ct);
    }
}

public class RecordValueValidator : Validator<RecordValueRequest>
{
    public RecordValueValidator()
    {
        RuleFor(x => x.ActualValue).NotNull().WithMessage("实测值不能为空");
    }
}

[MemoryPackable]
public partial class RecordValueRequest
{
    public double ActualValue { get; set; }
}
