using FastEndpoints;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Quality.Records;

/// <summary>检验记录列表端点（IQC/IPQC/OQC/首件/在线测试，按阶段筛选）</summary>
public class ListQualityRecordsEndpoint : MesEndpointWithoutRequest<List<QualityRecordResponse>>
{
    public override void Configure()
    {
        Get("/records");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ShiftLeader, MesRoles.ProductionManager);
        Summary(s => s.Summary = "查询检验记录列表（按检验阶段筛选）");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var stageStr = Query<string?>("stage", isRequired: false);
        var repo = Resolve<IQualityRecordRepository>();

        if (!string.IsNullOrWhiteSpace(stageStr) && Enum.TryParse<InspectionStage>(stageStr, true, out var stage))
        {
            var records = await repo.GetByStageAsync(stage, ct);
            Response = records.Select(QualityMapper.ToRecordResponse).ToList();
        }
        else
        {
            // 未指定阶段时默认返回 IQC + IPQC（页面主要场景）
            var iqc = await repo.GetByStageAsync(InspectionStage.Iq, ct);
            var ipqc = await repo.GetByStageAsync(InspectionStage.Ipqc, ct);
            Response = iqc.Concat(ipqc)
                .OrderByDescending(r => r.CreatedAt)
                .Select(QualityMapper.ToRecordResponse)
                .ToList();
        }

        await SendDualAsync(ct);
    }
}
