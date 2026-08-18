using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.Materials;
using MesAdmin.Application.Security;

namespace MesAdmin.Api.Features.Materials.Reject;

/// <summary>批次检验不合格端点（Received/Qualified → Rejected，T1.12）</summary>
public class RejectMaterialBatchEndpoint : MesEndpoint<RejectMaterialBatchRequest, MaterialBatchResponse>
{
    public override void Configure()
    {
        Post("/batches/{id}/reject");
        Group<MaterialGroup>();
        Roles(MesRoles.WarehouseClerk, MesRoles.ShiftLeader, MesRoles.QualityEngineer, MesRoles.ProductionManager);
        Summary(s => s.Summary = "批次检验不合格（隔离）");
    }

    public override async Task HandleAsync(RejectMaterialBatchRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var id))
        {
            AddError("id", "无效的物料批次 Id");
            ThrowIfAnyErrors();
        }

        var batch = await new RejectMaterialBatchCommand(id, req.InspectorId, req.Reason).ExecuteAsync(ct);
        Response = MaterialMapper.ToResponse(batch);
        await SendDualAsync(ct);
    }
}

[MemoryPackable]
public partial class RejectMaterialBatchRequest
{
    public string InspectorId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class RejectMaterialBatchValidator : Validator<RejectMaterialBatchRequest>
{
    public RejectMaterialBatchValidator()
    {
        RuleFor(x => x.InspectorId).NotEmpty().WithMessage("质检员工号不能为空");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("不合格原因不能为空");
    }
}
