namespace MesAdmin.Infrastructure.Reports;

/// <summary>
/// 报表引擎服务（T4.1）。
/// 协调：模板查找 → 数据聚合 → PDF 渲染 三阶段。
/// </summary>
public sealed class ReportEngineService
{
    private readonly ReportDataSourceService _dataSource;
    private readonly PdfReportGenerator _pdfGenerator;

    public ReportEngineService(
        ReportDataSourceService dataSource,
        PdfReportGenerator pdfGenerator)
    {
        _dataSource = dataSource;
        _pdfGenerator = pdfGenerator;
    }

    /// <summary>获取所有可用模板</summary>
    public IReadOnlyList<Application.Features.Reports.ReportTemplate> GetTemplates()
        => Application.Features.Reports.ReportTemplates.GetAll();

    /// <summary>按 ID 获取模板</summary>
    public Application.Features.Reports.ReportTemplate? GetTemplate(string id)
        => Application.Features.Reports.ReportTemplates.Get(id);

    /// <summary>生成报告 PDF（完整流程：模板 → 数据 → PDF）</summary>
    public async Task<byte[]> GenerateReportAsync(
        string templateId,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken ct = default)
    {
        var template = GetTemplate(templateId)
            ?? throw new ArgumentException($"模板不存在: {templateId}", nameof(templateId));

        var data = await _dataSource.AggregateAsync(template, start, end, ct);
        return _pdfGenerator.RenderTemplate(template, data);
    }

    /// <summary>生成报告 PDF（直接使用 ReportRenderData，跳过数据聚合）</summary>
    public byte[] RenderReport(
        string templateId,
        Application.Features.Reports.ReportRenderData data)
    {
        var template = GetTemplate(templateId)
            ?? throw new ArgumentException($"模板不存在: {templateId}", nameof(templateId));

        return _pdfGenerator.RenderTemplate(template, data);
    }
}
