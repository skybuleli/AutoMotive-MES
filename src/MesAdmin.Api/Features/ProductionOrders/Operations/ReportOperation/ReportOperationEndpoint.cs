using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.ProductionOrders.Operations.ReportOperation;

public class ReportOperationEndpoint : MesEndpoint<ReportOperationRequest, OperationResponse>
{
    public override void Configure()
    {
        Post("/{orderId}/operations/{sequence}/report");
        Group<ProductionOrderGroup>();
        Roles(MesRoles.ShiftLeader, MesRoles.ProductionManager);
        Summary(s => s.Summary = "操作员报工（扫码完成工序）");
    }

    public override async Task HandleAsync(ReportOperationRequest req, CancellationToken ct)
    {
        var orderIdStr = Route<string>("orderId")!;
        var seqStr = Route<string>("sequence")!;

        if (!Ulid.TryParse(orderIdStr, out var orderId))
        {
            AddError("orderId", "无效的工单 Id");
            ThrowIfAnyErrors();
        }

        if (!int.TryParse(seqStr, out var sequence))
        {
            AddError("sequence", "无效的工序序号");
            ThrowIfAnyErrors();
        }

        var op = await new ReportOperationCommand(orderId, sequence, req.OperatorId, req.EquipmentId, null).ExecuteAsync(ct);
        Response = OrderMapper.ToOperationResponse(op);
        await SendDualAsync(ct);
    }
}

[MemoryPackable]
public partial class ReportOperationRequest
{
    public string OperatorId { get; set; } = string.Empty;
    public string EquipmentId { get; set; } = string.Empty;
}

public class ReportOperationValidator : Validator<ReportOperationRequest>
{
    public ReportOperationValidator()
    {
        RuleFor(x => x.OperatorId).NotEmpty().WithMessage("操作员工号不能为空");
        RuleFor(x => x.EquipmentId).NotEmpty().WithMessage("设备编号不能为空");
    }
}
