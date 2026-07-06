using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Security;
using MesAdmin.Application.Features.Quality;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Quality.Ncr;

// ═══════════════════════════════════════════
//  POST /api/v1/quality/ncr — 创建 NCR
// ═══════════════════════════════════════════

public class CreateNcrEndpoint : MesEndpoint<CreateNcrRequest, NcrResponse>
{
    public override void Configure()
    {
        Post("/ncr");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "创建不合格品报告 (NCR)");
    }

    public override async Task HandleAsync(CreateNcrRequest req, CancellationToken ct)
    {
        if (!Ulid.TryParse(req.QualityRecordId, out var recordId))
        {
            AddError("QualityRecordId", "无效的检验记录 Id");
            ThrowIfAnyErrors();
        }

        if (!Enum.TryParse<NcrSeverity>(req.Severity, out var severity))
        {
            AddError("Severity", $"无效的严重等级: {req.Severity}");
            ThrowIfAnyErrors();
        }

        var ncr = await new CreateNcrCommand(
            recordId,
            req.Description,
            req.DefectQuantity,
            severity).ExecuteAsync(ct);

        Response = QualityMapper.ToNcrResponse(ncr);
        await SendDualAsync(ct);
    }
}

public class CreateNcrValidator : Validator<CreateNcrRequest>
{
    public CreateNcrValidator()
    {
        RuleFor(x => x.QualityRecordId).NotEmpty().WithMessage("检验记录 Id 不能为空");
        RuleFor(x => x.Description).NotEmpty().WithMessage("不合格描述不能为空");
        RuleFor(x => x.DefectQuantity).GreaterThan(0).WithMessage("不合格数量必须大于 0");
        RuleFor(x => x.Severity).NotEmpty().WithMessage("严重等级不能为空");
    }
}

[MemoryPackable]
public partial class CreateNcrRequest
{
    public string QualityRecordId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DefectQuantity { get; set; }
    public string Severity { get; set; } = "Major";
}

// ═══════════════════════════════════════════
//  GET /api/v1/quality/ncr — NCR 列表
// ═══════════════════════════════════════════

public class ListNcrEndpoint : MesEndpointWithoutRequest<List<NcrResponse>>
{
    public override void Configure()
    {
        Get("/ncr");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "查询 NCR 列表（支持按状态筛选）");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var statusStr = Query<string>("status");
        var repo = Resolve<INonConformanceReportRepository>();
        List<NonConformanceReport> ncrs;

        if (!string.IsNullOrWhiteSpace(statusStr) && Enum.TryParse<NcrStatus>(statusStr, out var status))
            ncrs = await repo.GetByStatusAsync(status, ct);
        else
            ncrs = await repo.GetByStatusAsync(NcrStatus.Open, ct);

        Response = ncrs.Select(QualityMapper.ToNcrResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/quality/ncr/{id}/review — 提交 MRB 评审
// ═══════════════════════════════════════════

public class SubmitNcrReviewEndpoint : MesEndpoint<SubmitNcrReviewRequest, NcrResponse>
{
    public override void Configure()
    {
        Post("/ncr/{id}/review");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "提交 NCR 给 MRB 评审");
    }

    public override async Task HandleAsync(SubmitNcrReviewRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var ncrId))
        {
            AddError("id", "无效的 NCR Id");
            ThrowIfAnyErrors();
        }

        var ncr = await new SubmitNcrForReviewCommand(ncrId, req.ReviewerId).ExecuteAsync(ct);
        Response = QualityMapper.ToNcrResponse(ncr);
        await SendDualAsync(ct);
    }
}

[MemoryPackable]
public partial class SubmitNcrReviewRequest
{
    public string ReviewerId { get; set; } = string.Empty;
}

// ═══════════════════════════════════════════
//  POST /api/v1/quality/ncr/{id}/disposition — 处置决定
// ═══════════════════════════════════════════

public class DispositionNcrEndpoint : MesEndpoint<DispositionNcrRequest, NcrResponse>
{
    public override void Configure()
    {
        Post("/ncr/{id}/disposition");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "MRB 做出 NCR 处置决定（让步接收/返工/返修/报废等）");
    }

    public override async Task HandleAsync(DispositionNcrRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var ncrId))
        {
            AddError("id", "无效的 NCR Id");
            ThrowIfAnyErrors();
        }

        if (!Enum.TryParse<NcrDisposition>(req.Disposition, out var disposition))
        {
            AddError("Disposition", $"无效的处置方式: {req.Disposition}");
            ThrowIfAnyErrors();
        }

        var ncr = await new DispositionNcrCommand(
            ncrId,
            disposition,
            req.Comments).ExecuteAsync(ct);

        Response = QualityMapper.ToNcrResponse(ncr);
        await SendDualAsync(ct);
    }
}

[MemoryPackable]
public partial class DispositionNcrRequest
{
    public string Disposition { get; set; } = "Concession";
    public string Comments { get; set; } = string.Empty;
}

// ═══════════════════════════════════════════
//  POST /api/v1/quality/ncr/{id}/close — 关闭 NCR
// ═══════════════════════════════════════════

public class CloseNcrEndpoint : MesEndpoint<CloseNcrRequest, NcrResponse>
{
    public override void Configure()
    {
        Post("/ncr/{id}/close");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "关闭 NCR");
    }

    public override async Task HandleAsync(CloseNcrRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var ncrId))
        {
            AddError("id", "无效的 NCR Id");
            ThrowIfAnyErrors();
        }

        var ncr = await new CloseNcrCommand(ncrId, req.Remarks).ExecuteAsync(ct);
        Response = QualityMapper.ToNcrResponse(ncr);
        await SendDualAsync(ct);
    }
}

[MemoryPackable]
public partial class CloseNcrRequest
{
    public string Remarks { get; set; } = string.Empty;
}
