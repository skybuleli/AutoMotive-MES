using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Application.Features.Traceability;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Security;

namespace MesAdmin.Api.Features.Traceability.Forward;

/// <summary>正向追溯查询端点（T1.22）：VIN/总成 S/N → 工单 → 零部件 → 原材料</summary>
public class ForwardTraceEndpoint : MesEndpointWithoutRequest<List<TraceabilityLinkResponse>>
{
    public override void Configure()
    {
        Get("/forward/{vinOrSerial}");
        Group<TraceabilityGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.QualityEngineer, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "正向追溯：VIN/总成 S/N → 全链路");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var vinOrSerial = Route<string>("vinOrSerial")!;
        var links = await new ForwardTraceQuery(vinOrSerial).ExecuteAsync(ct);
        Response = links.Select(TraceabilityMapper.ToResponse).ToList();
        await SendDualAsync(ct);
    }
}

public class ForwardTraceValidator : Validator<ForwardTraceQuery>
{
    public ForwardTraceValidator()
    {
        RuleFor(x => x.VinOrSerial).NotEmpty().WithMessage("VIN/序列号不能为空");
    }
}
