using FastEndpoints;
using MesAdmin.Application.Features.Reports;
using MesAdmin.Application.Security;
using MesAdmin.Infrastructure.Reports;

namespace MesAdmin.Api.Features.Quality.Reports;

public static class ReportEndpointExtensions
{
    /// <summary>发送 PDF 字节到响应流</summary>
    public static async Task SendPdfAsync(this HttpContext ctx, byte[] pdf, string fileName, CancellationToken ct)
    {
        ctx.Response.ContentType = "application/pdf";
        ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
        ctx.Response.Headers["Content-Length"] = pdf.Length.ToString();
        await ctx.Response.Body.WriteAsync(pdf, ct);
    }
}

// ═══════════════════════════════════════════════════════════
//  GET /api/v1/quality/reports/daily — 日报 PDF
// ═══════════════════════════════════════════════════════════

public class GetDailyReportEndpoint : EndpointWithoutRequest
{
    private readonly QualityReportService _reportService;

    public GetDailyReportEndpoint(QualityReportService reportService)
    {
        _reportService = reportService;
    }

    public override void Configure()
    {
        Get("/reports/daily");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.Inspector);
        Summary(s => s.Summary = "获取质量日报 PDF");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var yesterday = DateTimeOffset.Now.Date.AddDays(-1);
        var today = DateTimeOffset.Now.Date;
        var pdf = await _reportService.GenerateOnDemandAsync(
            ReportPeriod.Daily, yesterday, today, ct);

        await HttpContext.SendPdfAsync(pdf,
            $"AutoMES_Quality_Daily_{yesterday:yyyyMMdd}.pdf", ct);
    }
}

// ═══════════════════════════════════════════════════════════
//  GET /api/v1/quality/reports/weekly — 周报 PDF
// ═══════════════════════════════════════════════════════════

public class GetWeeklyReportEndpoint : EndpointWithoutRequest
{
    private readonly QualityReportService _reportService;

    public GetWeeklyReportEndpoint(QualityReportService reportService)
    {
        _reportService = reportService;
    }

    public override void Configure()
    {
        Get("/reports/weekly");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.Inspector);
        Summary(s => s.Summary = "获取质量周报 PDF");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.Now;
        var lastWeek = now.Date.AddDays((int)now.DayOfWeek * -1 - 7);
        var weekEnd = lastWeek.AddDays(7);
        var pdf = await _reportService.GenerateOnDemandAsync(
            ReportPeriod.Weekly, lastWeek, weekEnd, ct);

        await HttpContext.SendPdfAsync(pdf,
            $"AutoMES_Quality_Weekly_{lastWeek:yyyyMMdd}.pdf", ct);
    }
}

// ═══════════════════════════════════════════════════════════
//  GET /api/v1/quality/reports/monthly — 月报 PDF
// ═══════════════════════════════════════════════════════════

public class GetMonthlyReportEndpoint : EndpointWithoutRequest
{
    private readonly QualityReportService _reportService;

    public GetMonthlyReportEndpoint(QualityReportService reportService)
    {
        _reportService = reportService;
    }

    public override void Configure()
    {
        Get("/reports/monthly");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.Inspector);
        Summary(s => s.Summary = "获取质量月报 PDF");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.Now;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
        var lastMonth = monthStart.AddMonths(-1);
        var targetStart = new DateTimeOffset(lastMonth.Year, lastMonth.Month, 1, 0, 0, 0, now.Offset);
        var targetEnd = monthStart;

        var pdf = await _reportService.GenerateOnDemandAsync(
            ReportPeriod.Monthly, targetStart, targetEnd, ct);

        await HttpContext.SendPdfAsync(pdf,
            $"AutoMES_Quality_Monthly_{targetStart:yyyyMM}.pdf", ct);
    }
}

// ═══════════════════════════════════════════════════════════
//  POST /api/v1/quality/reports/custom — 自定义时间范围 PDF
// ═══════════════════════════════════════════════════════════

public class GetCustomReportEndpoint : Endpoint<CustomReportRequest>
{
    private readonly QualityReportService _reportService;

    public GetCustomReportEndpoint(QualityReportService reportService)
    {
        _reportService = reportService;
    }

    public override void Configure()
    {
        Post("/reports/custom");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "自定义时间范围质量报告 PDF");
    }

    public override async Task HandleAsync(CustomReportRequest req, CancellationToken ct)
    {
        if (req.EndDate <= req.StartDate)
        {
            AddError("时间范围无效：结束日期必须晚于开始日期");
            ThrowIfAnyErrors();
        }

        var period = (req.EndDate - req.StartDate).TotalDays switch
        {
            <= 2 => ReportPeriod.Daily,
            <= 14 => ReportPeriod.Weekly,
            _ => ReportPeriod.Monthly
        };

        var pdf = await _reportService.GenerateOnDemandAsync(
            period, req.StartDate, req.EndDate, ct);

        await HttpContext.SendPdfAsync(pdf,
            $"AutoMES_Quality_Custom_{req.StartDate:yyyyMMdd}_{req.EndDate:yyyyMMdd}.pdf", ct);
    }
}

public class CustomReportRequest
{
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }
}
