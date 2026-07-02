using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Security;
using MesAdmin.Application.Features.Inspections;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Inspections.Complete;

public class CompleteInspectionEndpoint : MesEndpoint<CompleteInspectionRequest, InspectionResponse>
{
    public override void Configure()
    {
        Post("/{inspectionId}/complete");
        Group<InspectionGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "完成首件检验（质量工程师审核放行）");
    }

    public override async Task HandleAsync(CompleteInspectionRequest req, CancellationToken ct)
    {
        var inspectionIdStr = Route<string>("inspectionId")!;
        if (!Ulid.TryParse(inspectionIdStr, out var inspectionId))
        {
            AddError("inspectionId", "无效的检验 Id");
            ThrowIfAnyErrors();
        }

        var inspection = await new CompleteInspectionCommand(inspectionId, req.InspectorId).ExecuteAsync(ct);
        Response = InspectionMapper.ToResponse(inspection);
        await SendDualAsync(ct);
    }
}

public class CompleteInspectionValidator : Validator<CompleteInspectionRequest>
{
    public CompleteInspectionValidator()
    {
        RuleFor(x => x.InspectorId).NotEmpty().WithMessage("审核人工号不能为空");
    }
}

[MemoryPackable]
public partial class CompleteInspectionRequest
{
    public string InspectorId { get; set; } = string.Empty;
}
