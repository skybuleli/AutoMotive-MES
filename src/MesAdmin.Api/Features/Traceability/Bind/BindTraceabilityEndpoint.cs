using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Application.Features.Traceability;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Traceability.Bind;

public class BindTraceabilityEndpoint : MesEndpoint<BindTraceabilityRequest, TraceabilityLinkResponse>
{
    public override void Configure()
    {
        Post("/bind");
        Group<TraceabilityGroup>();
        Roles(MesRoles.ShiftLeader, MesRoles.ProductionManager);
        Summary(s => s.Summary = "追溯绑定（扫码绑定 ECU/HCU/电机 S/N → 电磁阀批次 → 阀体批次）");
    }

    public override async Task HandleAsync(BindTraceabilityRequest req, CancellationToken ct)
    {
        if (!Ulid.TryParse(req.OrderId, out var orderId))
        {
            AddError(r => r.OrderId, "无效的工单 Id");
            ThrowIfAnyErrors();
        }

        var link = await new BindTraceabilityCommand(
            orderId,
            req.Level,
            req.VinOrSerial,
            req.ComponentBatch,
            req.MaterialBatch).ExecuteAsync(ct);

        Response = TraceabilityMapper.ToResponse(link);
        await SendCreatedDualAsync<BindTraceabilityEndpoint>(new { id = link.Id.ToString() }, ct);
    }
}

[MemoryPackable]
public partial class BindTraceabilityRequest
{
    public string OrderId { get; set; } = string.Empty;
    /// <summary>追溯层级（1=车辆VIN / 2=ESP总成S/N / 3=零部件 / 4=原材料）</summary>
    public TraceabilityLevel Level { get; set; }
    public string VinOrSerial { get; set; } = string.Empty;
    public string? ComponentBatch { get; set; }
    public string? MaterialBatch { get; set; }
}

public class BindTraceabilityValidator : Validator<BindTraceabilityRequest>
{
    public BindTraceabilityValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty().WithMessage("工单 Id 不能为空");
        RuleFor(x => x.Level).IsInEnum().WithMessage("追溯层级无效");
        RuleFor(x => x.VinOrSerial).NotEmpty().WithMessage("VIN/序列号不能为空");
    }
}
