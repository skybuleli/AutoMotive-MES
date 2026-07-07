using FastEndpoints;
using MesAdmin.Application.Features.Reports;
using MesAdmin.Application.Security;
using MesAdmin.Infrastructure.Reports;

namespace MesAdmin.Api.Features.Reports;

// ═══════════════════════════════════════════════════════════
//  GET /api/v1/reports/templates — 获取所有可用报表模板
// ═══════════════════════════════════════════════════════════

public class ListTemplatesEndpoint : EndpointWithoutRequest
{
    private readonly ReportEngineService _engine;

    public ListTemplatesEndpoint(ReportEngineService engine)
    {
        _engine = engine;
    }

    public override void Configure()
    {
        Get("/reports/templates");
        Group<ReportGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.Inspector, MesRoles.ProductionManager);
        Summary(s => s.Summary = "获取所有可用报表模板");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var templates = _engine.GetTemplates();
        await Send.OkAsync(templates.Select(t => new TemplateListItem(
            t.Id, t.Name, t.Description, t.Type.ToString(),
            t.Sections.Count, t.SupportsEmail, t.SupportsSchedule
        )).ToList(), ct);
    }
}

public sealed record TemplateListItem(
    string Id, string Name, string Description, string Type,
    int SectionCount, bool SupportsEmail, bool SupportsSchedule
);

// ═══════════════════════════════════════════════════════════
//  GET /api/v1/reports/templates/{id} — 获取模板详情
// ═══════════════════════════════════════════════════════════

public class GetTemplateEndpoint : EndpointWithoutRequest
{
    private readonly ReportEngineService _engine;

    public GetTemplateEndpoint(ReportEngineService engine)
    {
        _engine = engine;
    }

    public override void Configure()
    {
        Get("/reports/templates/{id}");
        Group<ReportGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ProductionManager);
        Summary(s => s.Summary = "获取报表模板详情");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<string>("id")!;
        var template = _engine.GetTemplate(id);
        if (template is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(new TemplateDetailResponse(
            template.Id, template.Name, template.Description,
            template.Type.ToString(),
            template.Sections.Select(s => new SectionItem(s.Id, s.Title, s.Layout.ToString())).ToList(),
            template.SupportsEmail, template.SupportsSchedule
        ), ct);
    }
}

public sealed record SectionItem(string Id, string Title, string Layout);

public sealed record TemplateDetailResponse(
    string Id, string Name, string Description, string Type,
    List<SectionItem> Sections, bool SupportsEmail, bool SupportsSchedule
);

// ═══════════════════════════════════════════════════════════
//  POST /api/v1/reports/templates/{id}/generate — 生成报表 PDF
// ═══════════════════════════════════════════════════════════

public class GenerateReportEndpoint : Endpoint<GenerateReportRequest, EmptyResponse>
{
    private readonly ReportEngineService _engine;

    public GenerateReportEndpoint(ReportEngineService engine)
    {
        _engine = engine;
    }

    public override void Configure()
    {
        Post("/reports/templates/{id}/generate");
        Group<ReportGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ProductionManager);
        Summary(s => s.Summary = "根据模板和时间范围生成报表 PDF");
    }

    public override async Task HandleAsync(GenerateReportRequest req, CancellationToken ct)
    {
        if (req.EndDate <= req.StartDate)
        {
            AddError("时间范围无效：结束日期必须晚于开始日期");
            ThrowIfAnyErrors();
        }

        try
        {
            var pdf = await _engine.GenerateReportAsync(req.Id, req.StartDate, req.EndDate, ct);

            var fileName = $"AutoMES_{req.Id}_{req.StartDate:yyyyMMdd}_{req.EndDate:yyyyMMdd}.pdf";
            HttpContext.Response.ContentType = "application/pdf";
            HttpContext.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
            HttpContext.Response.Headers["Content-Length"] = pdf.Length.ToString();
            await HttpContext.Response.Body.WriteAsync(pdf, ct);
        }
        catch (ArgumentException ex)
        {
            AddError(ex.Message);
            ThrowIfAnyErrors();
        }
    }
}

public class GenerateReportRequest
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }
}

// ═══════════════════════════════════════════════════════════
//  Report Group（路由前缀）
// ═══════════════════════════════════════════════════════════

public class ReportGroup : Group
{
    public ReportGroup()
    {
        Configure("api/v1", ep => ep.Description(x => x.WithTags("报表引擎")));
    }
}
