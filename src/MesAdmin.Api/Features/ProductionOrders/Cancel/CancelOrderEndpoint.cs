using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.ProductionOrders.Cancel;

public class CancelOrderEndpoint : MesEndpoint<CancelOrderRequest, ProductionOrderDetailResponse>
{
    public override void Configure()
    {
        Post("/{orderId}/cancel");
        Group<ProductionOrderGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "取消工单（生产开始前）");
    }

    public override async Task HandleAsync(CancelOrderRequest req, CancellationToken ct)
    {
        var orderIdStr = Route<string>("orderId")!;
        if (!Ulid.TryParse(orderIdStr, out var id))
        {
            AddError("orderId", "无效的工单 Id");
            ThrowIfAnyErrors();
        }

        var order = await new CancelOrderCommand(id, req.Reason).ExecuteAsync(ct);
        Response = OrderMapper.ToDetail(order);
        await SendDualAsync(ct);
    }
}

[MemoryPackable]
public partial class CancelOrderRequest
{
    /// <summary>取消原因（必填）</summary>
    public string Reason { get; set; } = string.Empty;
}

public class CancelOrderValidator : Validator<CancelOrderRequest>
{
    public CancelOrderValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("取消原因不能为空")
            .MaximumLength(256).WithMessage("取消原因不能超过 256 字符");
    }
}
