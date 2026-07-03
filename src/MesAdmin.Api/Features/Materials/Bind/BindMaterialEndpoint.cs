using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.Materials;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Materials.Bind;

/// <summary>投料批次绑定端点（T1.15 + T1.16 Poka-Yoke）</summary>
public class BindMaterialEndpoint : MesEndpoint<BindMaterialRequest, MaterialBindingResponse>
{
    public override void Configure()
    {
        Post("/bindings");
        Group<MaterialGroup>();
        Roles(MesRoles.ShiftLeader, MesRoles.ProductionManager);
        Summary(s => s.Summary = "投料批次绑定（物料批次→工单→产品S/N + Poka-Yoke 防错）");
    }

    public override async Task HandleAsync(BindMaterialRequest req, CancellationToken ct)
    {
        if (!Ulid.TryParse(req.OrderId, out var orderId))
        {
            AddError(r => r.OrderId, "无效的工单 Id");
            ThrowIfAnyErrors();
        }

        if (!Ulid.TryParse(req.MaterialBatchId, out var batchId))
        {
            AddError(r => r.MaterialBatchId, "无效的物料批次 Id");
            ThrowIfAnyErrors();
        }

        var binding = await new BindMaterialCommand(
            orderId,
            batchId,
            req.ProductSerial,
            req.Quantity,
            req.OperatorId).ExecuteAsync(ct);

        Response = MaterialMapper.ToResponse(binding);
        await SendCreatedDualAsync<BindMaterialEndpoint>(new { id = binding.Id.ToString() }, ct);
    }
}

[MemoryPackable]
public partial class BindMaterialRequest
{
    public string OrderId { get; set; } = string.Empty;
    public string MaterialBatchId { get; set; } = string.Empty;
    public string ProductSerial { get; set; } = string.Empty;
    public double Quantity { get; set; }
    public string OperatorId { get; set; } = string.Empty;
}

public class BindMaterialValidator : Validator<BindMaterialRequest>
{
    public BindMaterialValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty().WithMessage("工单 Id 不能为空");
        RuleFor(x => x.MaterialBatchId).NotEmpty().WithMessage("物料批次 Id 不能为空");
        RuleFor(x => x.ProductSerial).NotEmpty().WithMessage("产品序列号不能为空");
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("投料数量必须大于 0");
        RuleFor(x => x.OperatorId).NotEmpty().WithMessage("操作员工号不能为空");
    }
}
