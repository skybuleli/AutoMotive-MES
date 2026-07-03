using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.ProductionOrders.Complete;

public class CompleteOrderEndpoint : MesEndpoint<CompleteOrderRequest, ProductionOrderDetailResponse>
{
    public override void Configure()
    {
        Post("/{orderId}/complete");
        Group<ProductionOrderGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.ShiftLeader, MesRoles.QualityEngineer);
        Summary(s => s.Summary = "完工确认");
    }

    public override async Task HandleAsync(CompleteOrderRequest req, CancellationToken ct)
    {
        var orderIdStr = Route<string>("orderId")!;
        if (!Ulid.TryParse(orderIdStr, out var id))
        {
            AddError("orderId", "无效的工单 Id");
            ThrowIfAnyErrors();
        }

        var order = await new CompleteOrderCommand(id, req.QualifiedQuantity, req.DefectiveQuantity).ExecuteAsync(ct);
        Response = OrderMapper.ToDetail(order);
        await SendDualAsync(ct);
    }
}

[MemoryPackable]
public partial class CompleteOrderRequest
{
    public int QualifiedQuantity { get; set; }
    public int DefectiveQuantity { get; set; }
}

public class CompleteOrderValidator : Validator<CompleteOrderRequest>
{
    public CompleteOrderValidator()
    {
        RuleFor(x => x.QualifiedQuantity).GreaterThanOrEqualTo(0).WithMessage("合格数量不能为负");
        RuleFor(x => x.DefectiveQuantity).GreaterThanOrEqualTo(0).WithMessage("不良数量不能为负");
    }
}
