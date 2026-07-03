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

        // T1.8 质量审核人工号：从 JWT claims 读取（工号），回退到请求体
        var reviewerId = req.ReviewerId;
        if (string.IsNullOrWhiteSpace(reviewerId))
        {
            reviewerId = User.FindFirst("employee_id")?.Value
                         ?? User.Identity?.Name
                         ?? "UNKNOWN";
        }

        var order = await new CompleteOrderCommand(id, req.QualifiedQuantity, req.DefectiveQuantity, reviewerId).ExecuteAsync(ct);
        Response = OrderMapper.ToDetail(order);
        await SendDualAsync(ct);
    }
}

[MemoryPackable]
public partial class CompleteOrderRequest
{
    public int QualifiedQuantity { get; set; }
    public int DefectiveQuantity { get; set; }
    /// <summary>质量审核人工号（可选，缺省从 JWT 读取）</summary>
    public string? ReviewerId { get; set; }
}

public class CompleteOrderValidator : Validator<CompleteOrderRequest>
{
    public CompleteOrderValidator()
    {
        RuleFor(x => x.QualifiedQuantity).GreaterThanOrEqualTo(0).WithMessage("合格数量不能为负");
        RuleFor(x => x.DefectiveQuantity).GreaterThanOrEqualTo(0).WithMessage("不良数量不能为负");
    }
}
