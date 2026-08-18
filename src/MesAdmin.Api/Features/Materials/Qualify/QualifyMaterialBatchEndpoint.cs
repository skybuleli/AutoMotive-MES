using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.Materials;
using MesAdmin.Application.Security;

namespace MesAdmin.Api.Features.Materials.Qualify;

/// <summary>批次检验合格端点（Received → Qualified，T1.12）</summary>
public class QualifyMaterialBatchEndpoint : MesEndpoint<QualifyMaterialBatchRequest, MaterialBatchResponse>
{
    public override void Configure()
    {
        Post("/batches/{id}/qualify");
        Group<MaterialGroup>();
        Roles(MesRoles.WarehouseClerk, MesRoles.ShiftLeader, MesRoles.QualityEngineer, MesRoles.ProductionManager);
        Summary(s => s.Summary = "批次检验合格（标记可用）");
    }

    public override async Task HandleAsync(QualifyMaterialBatchRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var id))
        {
            AddError("id", "无效的物料批次 Id");
            ThrowIfAnyErrors();
        }

        var batch = await new QualifyMaterialBatchCommand(id, req.InspectorId).ExecuteAsync(ct);
        Response = MaterialMapper.ToResponse(batch);
        await SendDualAsync(ct);
    }
}

[MemoryPackable]
public partial class QualifyMaterialBatchRequest
{
    public string InspectorId { get; set; } = string.Empty;
}

public class QualifyMaterialBatchValidator : Validator<QualifyMaterialBatchRequest>
{
    public QualifyMaterialBatchValidator()
    {
        RuleFor(x => x.InspectorId).NotEmpty().WithMessage("质检员工号不能为空");
    }
}
