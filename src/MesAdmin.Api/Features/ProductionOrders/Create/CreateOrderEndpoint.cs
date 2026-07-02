using FastEndpoints;
using FluentValidation;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;
using MesAdmin.Api.Features.ProductionOrders.GetById;

namespace MesAdmin.Api.Features.ProductionOrders.Create;

public class CreateOrderEndpoint : MesEndpoint<CreateOrderRequest, ProductionOrderSummaryResponse>
{
    public override void Configure()
    {
        Post("/");
        Group<ProductionOrderGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "创建生产工单");
    }

    public override async Task HandleAsync(CreateOrderRequest req, CancellationToken ct)
    {
        var order = await new CreateOrderCommand(
            req.ProductCode,
            req.BomVersion,
            Ulid.Parse(req.RoutingId),
            req.PlannedQuantity,
            req.Priority).ExecuteAsync(ct);

        Response = OrderMapper.ToSummary(order);
        await SendCreatedDualAsync<GetOrderByIdEndpoint>(new { orderId = order.Id.ToString() }, ct);
    }
}

public class CreateOrderValidator : Validator<CreateOrderRequest>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.ProductCode)
            .NotEmpty().WithMessage("产品编码不能为空")
            .Must(x => x.Trim().ToUpperInvariant() is "ESP-9.0" or "ESP-9.1")
            .WithMessage("产品编码仅支持 ESP-9.0 / ESP-9.1");

        RuleFor(x => x.BomVersion).NotEmpty().WithMessage("BOM 版本不能为空");
        RuleFor(x => x.RoutingId).NotEmpty().WithMessage("工艺路线 ID 不能为空");
        RuleFor(x => x.PlannedQuantity).GreaterThan(0).WithMessage("计划数量必须大于 0");
        RuleFor(x => x.Priority).InclusiveBetween((short)1, (short)2).WithMessage("优先级仅支持 1 或 2");
    }
}
