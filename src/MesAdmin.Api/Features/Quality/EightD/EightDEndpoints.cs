using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Security;
using MesAdmin.Application.Features.Quality;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Quality.EightD;

// ═══════════════════════════════════════════
//  POST /api/v1/quality/8d — 创建 8D 报告
// ═══════════════════════════════════════════

public class CreateEightDEndpoint : MesEndpoint<CreateEightDRequest, EightDReportResponse>
{
    public override void Configure()
    {
        Post("/8d");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "创建 8D 问题解决报告");
    }

    public override async Task HandleAsync(CreateEightDRequest req, CancellationToken ct)
    {
        Ulid? ncrId = req.NcrId is not null && Ulid.TryParse(req.NcrId, out var parsed) ? parsed : null;
        var report = await new CreateEightDReportCommand(
            ncrId,
            req.NcrNumber,
            req.Title,
            req.ProductCode,
            req.ProductName).ExecuteAsync(ct);

        Response = QualityMapper.ToEightDResponse(report);
        await SendDualAsync(ct);
    }
}

public class CreateEightDValidator : Validator<CreateEightDRequest>
{
    public CreateEightDValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("问题标题不能为空");
        RuleFor(x => x.ProductCode).NotEmpty().WithMessage("产品编码不能为空");
    }
}

[MemoryPackable]
public partial class CreateEightDRequest
{
    public string? NcrId { get; set; }
    public string? NcrNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
}

// ═══════════════════════════════════════════
//  PUT /api/v1/quality/8d/{id} — 更新 8D 步骤
// ═══════════════════════════════════════════

public class UpdateEightDEndpoint : MesEndpoint<UpdateEightDRequest, EightDReportResponse>
{
    public override void Configure()
    {
        Put("/8d/{id}");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "更新 8D 报告步骤内容（D1-D7）");
    }

    public override async Task HandleAsync(UpdateEightDRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var reportId))
        {
            AddError("id", "无效的 8D 报告 Id");
            ThrowIfAnyErrors();
        }

        var report = await new UpdateEightDReportCommand(
            reportId,
            req.TeamLeader,
            req.TeamMembers,
            req.ProblemDescription,
            req.ContainmentAction,
            req.RootCauseAnalysis,
            req.RootCause,
            req.CorrectiveAction,
            req.CorrectiveActionOwner,
            req.CorrectiveActionDueDate,
            req.VerificationMethod,
            req.VerificationResult,
            req.PreventiveAction,
            req.CompletedStep).ExecuteAsync(ct);

        Response = QualityMapper.ToEightDResponse(report);
        await SendDualAsync(ct);
    }
}

[MemoryPackable]
public partial class UpdateEightDRequest
{
    // D1: 团队
    public string? TeamLeader { get; set; }
    public string? TeamMembers { get; set; }
    // D2: 问题描述
    public string? ProblemDescription { get; set; }
    // D3: 围堵
    public string? ContainmentAction { get; set; }
    // D4: 根因
    public string? RootCauseAnalysis { get; set; }
    public string? RootCause { get; set; }
    // D5: 纠正措施
    public string? CorrectiveAction { get; set; }
    public string? CorrectiveActionOwner { get; set; }
    public DateTimeOffset? CorrectiveActionDueDate { get; set; }
    // D6: 验证
    public string? VerificationMethod { get; set; }
    public string? VerificationResult { get; set; }
    // D7: 预防措施
    public string? PreventiveAction { get; set; }
    /// <summary>已完成的步骤编号（1-7）</summary>
    public int CompletedStep { get; set; }
}

// ═══════════════════════════════════════════
//  POST /api/v1/quality/8d/{id}/close — 关闭 8D
// ═══════════════════════════════════════════

public class CloseEightDEndpoint : MesEndpoint<CloseEightDRequest, EightDReportResponse>
{
    public override void Configure()
    {
        Post("/8d/{id}/close");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "关闭 8D 报告（D8: 总结表彰）");
    }

    public override async Task HandleAsync(CloseEightDRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var reportId))
        {
            AddError("id", "无效的 8D 报告 Id");
            ThrowIfAnyErrors();
        }

        var report = await new CloseEightDReportCommand(reportId, req.Summary).ExecuteAsync(ct);
        Response = QualityMapper.ToEightDResponse(report);
        await SendDualAsync(ct);
    }
}

[MemoryPackable]
public partial class CloseEightDRequest
{
    public string Summary { get; set; } = string.Empty;
}

// ═══════════════════════════════════════════
//  GET /api/v1/quality/8d — 8D 报告列表
// ═══════════════════════════════════════════

public class ListEightDEndpoint : MesEndpointWithoutRequest<List<EightDReportResponse>>
{
    public override void Configure()
    {
        Get("/8d");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "查询 8D 报告列表（支持按状态筛选）");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var statusStr = Query<string?>("status", isRequired: false);
        var repo = Resolve<IEightDReportRepository>();
        List<EightDReport> reports;

        if (!string.IsNullOrWhiteSpace(statusStr) && Enum.TryParse<EightDStatus>(statusStr, out var status))
            reports = await repo.GetByStatusAsync(status, ct);
        else
            reports = await repo.GetByStatusAsync(EightDStatus.Open, ct);

        Response = reports.Select(QualityMapper.ToEightDResponse).ToList();
        await SendDualAsync(ct);
    }
}
