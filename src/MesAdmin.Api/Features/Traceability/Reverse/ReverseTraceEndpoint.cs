using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Application.Features.Traceability;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Security;

namespace MesAdmin.Api.Features.Traceability.Reverse;

/// <summary>
/// 反向追溯查询端点（T1.23）：原材料/零部件批次 → 所有总成 S/N → 所有 VIN。
/// 用于召回场景：某批次原材料 → 影响 1247 件总成 → 导出 VIN 清单发整车厂。
/// </summary>
public class ReverseTraceEndpoint : MesEndpointWithoutRequest<List<TraceabilityLinkResponse>>
{
    public override void Configure()
    {
        Get("/reverse/{batchType}/{batch}");
        Group<TraceabilityGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.QualityEngineer, MesRoles.SupplierQualityEngineer);
        Summary(s => s.Summary = "反向追溯：批次 → 所有总成 S/N → 所有 VIN（召回场景）");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var batchTypeStr = Route<string>("batchType")!;
        var batch = Route<string>("batch")!;

        if (!Enum.TryParse<TraceabilityBatchType>(batchTypeStr, ignoreCase: true, out var batchType))
        {
            AddError("batchType", $"批次类型无效，支持：{nameof(TraceabilityBatchType.Component)} / {nameof(TraceabilityBatchType.Material)}");
            ThrowIfAnyErrors();
        }

        var links = await new ReverseTraceQuery(batch, batchType).ExecuteAsync(ct);
        Response = links.Select(TraceabilityMapper.ToResponse).ToList();
        await SendDualAsync(ct);
    }
}
