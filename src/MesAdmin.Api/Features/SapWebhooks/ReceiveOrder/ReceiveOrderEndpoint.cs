using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Features.ProductionOrders;
using MesAdmin.Api.Features.ProductionOrders.GetById;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.SapWebhooks.ReceiveOrder;

public class ReceiveOrderEndpoint : MesEndpoint<SapProductionOrderRequest, ProductionOrderSummaryResponse>
{
    public override void Configure()
    {
        Post("/production-orders");
        Group<SapWebhookGroup>();
        PreProcessors(new SapSignaturePreProcessor<SapProductionOrderRequest>());
        Summary(s =>
        {
            s.Summary = "SAP 推送生产工单";
            s.Description = "接收 SAP 推送的工单数据，校验产品编码+BOM版本+工艺路线版本后创建本地工单。需在 X-Sap-Signature 头中提供 HMAC-SHA256 签名。";
        });
    }

    public override async Task HandleAsync(SapProductionOrderRequest req, CancellationToken ct)
    {
        // Ulid 校验（Validator 不含此逻辑因为需要领域类型）
        if (!Ulid.TryParse(req.RoutingId, out var routingId))
        {
            Logger.LogWarning("SAP 工单拒单：工艺路线 ID {RoutingId} 无效", req.RoutingId);
            AddError(r => r.RoutingId, $"工艺路线 ID 无效：{req.RoutingId}");
            ThrowIfAnyErrors();
        }

        try
        {
            var order = await new CreateOrderCommand(
                req.ProductCode,
                req.BomVersion,
                routingId,
                req.PlannedQuantity,
                req.Priority).ExecuteAsync(ct);

            Logger.LogInformation("SAP 工单已创建：{OrderNumber}（来源：{ExternalOrderNumber}）",
                order.OrderNumber, req.ExternalOrderNumber);

            Response = OrderMapper.ToSummary(order);
            await SendCreatedDualAsync<GetOrderByIdEndpoint>(new { orderId = order.Id.ToString() }, ct);
        }
        catch (ArgumentException ex)
        {
            Logger.LogWarning(ex, "SAP 工单创建失败：参数校验不通过");
            AddError(ex.Message);
            ThrowIfAnyErrors();
        }
    }
}

[MemoryPackable]
public partial class SapProductionOrderRequest
{
    public string ProductCode { get; set; } = string.Empty;
    public string BomVersion { get; set; } = string.Empty;
    public string RoutingId { get; set; } = string.Empty;
    public int PlannedQuantity { get; set; }
    public short Priority { get; set; } = 1;
    public string? ExternalOrderNumber { get; set; }
}

public class SapWebhookValidator : Validator<SapProductionOrderRequest>
{
    public SapWebhookValidator()
    {
        RuleFor(x => x.ProductCode)
            .NotEmpty().WithMessage("产品编码不能为空")
            .Must(x => x.Trim().ToUpperInvariant() is "ESP-9.0" or "ESP-9.1")
            .WithMessage("产品编码仅支持 ESP-9.0 / ESP-9.1");

        RuleFor(x => x.BomVersion)
            .NotEmpty().WithMessage("BOM 版本不能为空");

        RuleFor(x => x.RoutingId)
            .NotEmpty().WithMessage("工艺路线 ID 不能为空");

        RuleFor(x => x.PlannedQuantity)
            .GreaterThan(0).WithMessage("计划数量必须大于 0");

        RuleFor(x => x.Priority)
            .InclusiveBetween((short)1, (short)2).WithMessage("优先级仅支持 1 或 2");
    }
}
