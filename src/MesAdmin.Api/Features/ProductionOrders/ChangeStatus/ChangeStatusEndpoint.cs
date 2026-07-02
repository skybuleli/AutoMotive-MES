using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.ProductionOrders.ChangeStatus;

public class ChangeStatusEndpoint : MesEndpoint<ChangeStatusRequest, ProductionOrderDetailResponse>
{
    public override void Configure()
    {
        Patch("/{orderId}/status");
        Group<ProductionOrderGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.ShiftLeader, MesRoles.QualityEngineer);
        Summary(s => s.Summary = "变更工单状态（放行/开工/关闭）");
    }

    public override async Task HandleAsync(ChangeStatusRequest req, CancellationToken ct)
    {
        var orderIdStr = Route<string>("orderId")!;
        if (!Ulid.TryParse(orderIdStr, out var id))
        {
            AddError("orderId", "无效的工单 Id");
            ThrowIfAnyErrors();
        }

        // 拆分为三个独立命令，保持 CQRS 单一职责；端点路由契约不变
        var order = req.Status.Trim().ToLowerInvariant() switch
        {
            "released" => await new ReleaseOrderCommand(id).ExecuteAsync(ct),
            "inprogress" => await new StartOrderCommand(id).ExecuteAsync(ct),
            "closed" => await new CloseOrderCommand(id).ExecuteAsync(ct),
            "completed" => throw new ArgumentException("请使用 /complete 接口提交完工数量"),
            _ => throw new ArgumentException("状态仅支持 Released / InProgress / Closed"),
        };

        Response = OrderMapper.ToDetail(order);
        await SendDualAsync(ct);
    }
}

[MemoryPackable]
public partial class ChangeStatusRequest
{
    public string Status { get; set; } = string.Empty;
}

public class ChangeStatusValidator : Validator<ChangeStatusRequest>
{
    public ChangeStatusValidator()
    {
        RuleFor(x => x.Status)
            .NotEmpty().WithMessage("状态不能为空")
            .Must(s => s.Trim().ToLowerInvariant() is "released" or "inprogress" or "closed")
            .WithMessage("状态仅支持 Released / InProgress / Closed");
    }
}
