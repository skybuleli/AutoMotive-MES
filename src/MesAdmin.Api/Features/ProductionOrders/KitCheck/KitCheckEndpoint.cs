using FastEndpoints;
using FluentValidation;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.ProductionOrders.KitCheck;

public class KitCheckEndpoint : MesEndpointWithoutRequest<KitCheckResponse>
{
    public override void Configure()
    {
        Post("/{orderId}/kit-check");
        Group<ProductionOrderGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "物料齐套检查（BOM 展开→库存查询→缺料JIT拉动→齐套转Released）");
        Description(d => d
            .Produces<KitCheckResponse>(200, "application/json")
            .ProducesProblem(400, "application/problem+json")
            .ProducesProblem(404, "application/problem+json"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var orderIdStr = Route<string>("orderId")!;
        if (!Ulid.TryParse(orderIdStr, out var id))
        {
            AddError("orderId", "无效的工单 Id");
            ThrowIfAnyErrors();
        }

        var result = await new KitCheckCommand(id).ExecuteAsync(ct);

        Response = new KitCheckResponse(
            result.IsPassed,
            result.Items.Select(i => new KitCheckItemResponse(
                i.MaterialCode,
                i.MaterialName,
                i.RequiredQuantity,
                i.AvailableQuantity,
                i.ShortageQuantity,
                i.Unit,
                i.IsCritical
            )).ToList(),
            result.JitPullSignalIds.Select(id => id.ToString()).ToList());

        await SendDualAsync(ct);
    }
}

/// <summary>齐套检查响应。</summary>
[MemoryPack.MemoryPackable]
public partial record KitCheckResponse(
    bool IsPassed,
    List<KitCheckItemResponse> Items,
    List<string> JitPullSignalIds);

/// <summary>齐套检查单项结果。</summary>
[MemoryPack.MemoryPackable]
public partial record KitCheckItemResponse(
    string MaterialCode,
    string MaterialName,
    double RequiredQuantity,
    double AvailableQuantity,
    double ShortageQuantity,
    string Unit,
    bool IsCritical);
