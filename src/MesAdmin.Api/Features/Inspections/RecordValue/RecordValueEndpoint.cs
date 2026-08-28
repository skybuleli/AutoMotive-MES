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

        Ulid? gaugeId = null;
        if (!string.IsNullOrWhiteSpace(req.GaugeId))
        {
            if (!Ulid.TryParse(req.GaugeId, out var parsed))
            {
                AddError("GaugeId", "无效的量具 Id");
                ThrowIfAnyErrors();
            }
            gaugeId = parsed;
        }

        var inspection = await new RecordInspectionValueCommand(inspectionId, characteristicCode, req.ActualValue, gaugeId).ExecuteAsync(ct);
        Response = InspectionMapper.ToResponse(inspection);
        await SendDualAsync(ct);
    }
}

public class RecordValueValidator : Validator<RecordValueRequest>
{
    public RecordValueValidator()
    {
        RuleFor(x => x.ActualValue).NotNull().WithMessage("实测值不能为空");
        RuleFor(x => x.GaugeId).NotEmpty().WithMessage("量具不能为空（S02 · 请在校准有效期内的量具中选择）");
    }
}

[MemoryPackable]
public partial class RecordValueRequest
{
    public double ActualValue { get; set; }

    /// <summary>量具 Id（S02 · IATF 16949 计量溯源，必填）</summary>
    public string? GaugeId { get; set; }
}
