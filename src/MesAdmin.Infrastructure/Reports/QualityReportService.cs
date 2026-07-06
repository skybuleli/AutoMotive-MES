using System.Globalization;
using FluentEmail.Core;
using MesAdmin.Application.Features.Reports;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Reports;

/// <summary>
/// 质量报表后台服务（T2.9）。
/// - 日报：每日 06:00 自动生成前一日 Cpk 报告
/// - 周报：每周一 06:00 自动生成前一周趋势报告
/// - 月报：每月 1日 06:00 自动生成上月 PPM/质量成本报告
/// - 支持 API 触发即时生成
/// </summary>
public sealed class QualityReportService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PdfReportGenerator _pdfGenerator;
    private readonly IConfiguration _config;
    private readonly ILogger<QualityReportService> _logger;
    private readonly IFluentEmailFactory _emailFactory;

    private static readonly TimeSpan[] DailyCheckIntervals = [TimeSpan.FromMinutes(1)]; // 启动后 1 分钟首次检查

    public QualityReportService(
        IServiceScopeFactory scopeFactory,
        PdfReportGenerator pdfGenerator,
        IConfiguration config,
        IFluentEmailFactory emailFactory,
        ILogger<QualityReportService> logger)
    {
        _scopeFactory = scopeFactory;
        _pdfGenerator = pdfGenerator;
        _config = config;
        _emailFactory = emailFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.ZLogInformation($"质量报表服务已启动");

        // 首次启动延迟，等待数据库就绪
        await Task.Delay(DailyCheckIntervals[0], ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.Now;
                var emailEnabled = _config.GetValue<bool>("QualityReports:Email:Enabled");

                // 日报：每日 06:00
                if (emailEnabled && now.Hour == 6 && now.Minute == 0)
                {
                    var yesterday = now.Date.AddDays(-1);
                    await GenerateAndSendReportAsync(ReportPeriod.Daily,
                        yesterday, now.Date, "daily", ct);
                }

                // 周报：每周一 06:00
                if (emailEnabled && now.DayOfWeek == DayOfWeek.Monday && now.Hour == 6 && now.Minute == 0)
                {
                    var lastWeek = now.Date.AddDays(-7);
                    await GenerateAndSendReportAsync(ReportPeriod.Weekly,
                        lastWeek, now.Date, "weekly", ct);
                }

                // 月报：每月 1日 06:00
                if (emailEnabled && now.Day == 1 && now.Hour == 6 && now.Minute == 0)
                {
                    var lastMonth = now.Date.AddMonths(-1);
                    var monthStart = new DateTimeOffset(lastMonth.Year, lastMonth.Month, 1, 0, 0, 0, now.Offset);
                    await GenerateAndSendReportAsync(ReportPeriod.Monthly,
                        monthStart, now.Date, "monthly", ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.ZLogError(ex, $"质量报表定时生成失败");
            }

            // 每 60s 检查一次时间条件
            await Task.Delay(TimeSpan.FromSeconds(60), ct);
        }
    }

    /// <summary>即时生成报告（API 调用）</summary>
    public async Task<byte[]> GenerateOnDemandAsync(
        ReportPeriod period, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        var data = await AggregateReportDataAsync(period, start, end, ct);
        return _pdfGenerator.GenerateReportPdf(data);
    }

    /// <summary>即时生成并发送报告</summary>
    public async Task GenerateAndSendReportAsync(
        ReportPeriod period, DateTimeOffset start, DateTimeOffset end,
        string key, CancellationToken ct = default)
    {
        var data = await AggregateReportDataAsync(period, start, end, ct);
        var pdfBytes = _pdfGenerator.GenerateReportPdf(data);

        var recipients = _config.GetSection("QualityReports:Email:Recipients").Get<string[]>();
        if (recipients is null || recipients.Length == 0)
        {
            _logger.ZLogWarning($"质量报表收件人未配置，跳过邮件发送");
            return;
        }

        var periodLabel = period switch
        {
            ReportPeriod.Daily => $"日报 {start:yyyy-MM-dd}",
            ReportPeriod.Weekly => $"周报 {start:yyyy-MM-dd} 至 {end:yyyy-MM-dd}",
            ReportPeriod.Monthly => $"月报 {start:yyyy-MM}",
            _ => "质量报告"
        };

        var email = _emailFactory.Create();
        email.To(string.Join(";", recipients));
        email.Subject($"[AutoMES] 质量{periodLabel} — 一次合格率 {data.FirstPassYield:F1}% | Cpk {GetBestCpk(data)}");
        email.Body($@"
<h2>AutoMES 博世 ESP 质量管理系统 — {periodLabel}</h2>
<hr/>
<h3>📊 概览</h3>
<ul>
  <li><b>检验总数：</b>{data.TotalInspections:N0}</li>
  <li><b>一次合格率：</b>{data.FirstPassYield:F1}%</li>
  <li><b>不合格数：</b>{data.FailedInspections:N0}</li>
  <li><b>NCR 未关闭：</b>{data.OpenNcrs}</li>
  <li><b>8D 关闭率：</b>{data.EightDClosureRate:F1}%</li>
</ul>
<h3>📈 SPC 能力汇总</h3>
<ul>
{string.Join("", data.SpcSummaries.Values.OrderBy(s => s.CharacteristicCode).Select(s =>
    $"  <li><b>{s.CharacteristicCode}</b> ({s.CharacteristicName}): 均值={s.Mean:F4}, Cpk={s.Cpk?.ToString("F4") ?? "N/A"}, 样本数={s.SampleCount}</li>\n"))}
</ul>
<p>详细报告请查看附件 PDF。</p>
<hr/>
<p style='color: #888; font-size: 0.8em;'>
  AutoMES 自动生成 · {data.GeneratedAt.ToLocalTime():yyyy-MM-dd HH:mm}<br/>
  博世 ESP® 制动系统质量管理系统
</p>
", isHtml: true);
        email.Attach(new FluentEmail.Core.Models.Attachment
        {
            Filename = $"AutoMES_Quality_{key}_{start:yyyyMMdd}.pdf",
            Data = new MemoryStream(pdfBytes),
            ContentType = "application/pdf"
        });

        await email.SendAsync(ct);
        _logger.ZLogInformation($"质量{periodLabel}邮件已发送至 {string.Join(", ", recipients)}");
    }

    /// <summary>从数据库聚合质量报表数据</summary>
    public async Task<QualityReportData> AggregateReportDataAsync(
        ReportPeriod period, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var recordRepo = scope.ServiceProvider.GetRequiredService<IQualityRecordRepository>();
        var ncrRepo = scope.ServiceProvider.GetRequiredService<INonConformanceReportRepository>();
        var sampleRepo = scope.ServiceProvider.GetRequiredService<ISpcSampleRepository>();
        var alertRepo = scope.ServiceProvider.GetRequiredService<ISpcRuleAlertRepository>();
        var eightDRepo = scope.ServiceProvider.GetRequiredService<IEightDReportRepository>();
        var planRepo = scope.ServiceProvider.GetRequiredService<IInspectionPlanRepository>();

        var now = DateTimeOffset.UtcNow;

        // ── 产量数据 ──
        var allRecords = await recordRepo.GetByStageAsync(InspectionStage.Ipqc, ct);
        var dateFiltered = allRecords
            .Where(r => r.CreatedAt >= start && r.CreatedAt <= end)
            .ToList();

        var totalInspections = dateFiltered.Count;
        var passed = dateFiltered.Count(r => r.Verdict == InspectionVerdict.Passed);
        var failed = dateFiltered.Count(r => r.Verdict == InspectionVerdict.Failed);
        var firstPassYield = totalInspections > 0 ? (double)passed / totalInspections * 100 : 100.0;

        // ── 不良品分布 ──
        var allNcrs = await ncrRepo.GetByProductCodeAsync("ESP-9.0", ct);
        var periodNcrs = allNcrs.Where(n => n.CreatedAt >= start && n.CreatedAt <= end).ToList();
        var openNcrs = periodNcrs.Count(n => n.Status != NcrStatus.Closed);

        var defectDistribution = new Dictionary<string, int>();
        foreach (var ncr in periodNcrs)
        {
            if (!defectDistribution.ContainsKey(ncr.Description))
                defectDistribution[ncr.Description] = 0;
            defectDistribution[ncr.Description]++;
        }

        // ── SPC 能力汇总 ──
        var plans = await planRepo.GetEnabledAsync(ct);
        var charCodes = plans
            .SelectMany(p => p.Characteristics)
            .Where(c => c.EnableSpc)
            .Select(c => c.CharacteristicCode)
            .Distinct()
            .ToList();

        var spcSummaries = new Dictionary<string, SpcSummary>();
        foreach (var code in charCodes)
        {
            var samples = await sampleRepo.GetByCharacteristicAsync(code, 100, ct);
            var periodSamples = samples.Where(s => s.CollectedAt >= start && s.CollectedAt <= end).ToList();

            if (periodSamples.Count == 0) continue;

            var values = periodSamples.SelectMany(s => s.Values).ToArray();
            var mean = values.Average();
            var stdDev = CalculateStdDev(values, mean);

            // 获取控制限
            var planChar = plans
                .SelectMany(p => p.Characteristics)
                .FirstOrDefault(c => c.CharacteristicCode == code);

            var usl = planChar?.UpperSpecLimit;
            var lsl = planChar?.LowerSpecLimit;

            // Cpk = min((USL - mean) / 3σ, (mean - LSL) / 3σ)
            double? cpk = null;
            if (usl.HasValue && lsl.HasValue && stdDev > 0)
            {
                var cpu = (usl.Value - mean) / (3 * stdDev);
                var cpl = (mean - lsl.Value) / (3 * stdDev);
                cpk = Math.Min(cpu, cpl);
            }

            // 告警统计
            var alerts = await alertRepo.GetByCharacteristicAsync(code, 50, ct);
            var periodAlerts = alerts.Where(a => a.CreatedAt >= start && a.CreatedAt <= end).ToList();

            spcSummaries[code] = new SpcSummary(
                code,
                planChar?.CharacteristicName ?? code,
                periodSamples.Count,
                mean,
                stdDev,
                cpk,
                cpk, // Ppk ≈ Cpk for this implementation
                planChar?.UpperControlLimit,
                planChar?.LowerControlLimit,
                periodAlerts.Count,
                periodAlerts.Count(a => !a.IsAcknowledged));
        }

        // ── 8D 状态 ──
        var all8Ds = await eightDRepo.GetByProductCodeAsync("ESP-9.0", ct);
        var period8Ds = all8Ds.Where(r => r.CreatedAt >= start && r.CreatedAt <= end).ToList();
        var closed8Ds = period8Ds.Count(r => r.Status == EightDStatus.Closed);
        var eightDClosureRate = period8Ds.Count > 0 ? (double)closed8Ds / period8Ds.Count * 100 : 100.0;

        return new QualityReportData(
            period,
            start,
            end,
            now,
            totalInspections,
            passed,
            failed,
            firstPassYield,
            periodNcrs.Count,
            openNcrs,
            defectDistribution,
            spcSummaries,
            SupplierSummaries: null,    // 供应商质量数据需集成 SAP 后补充
            QualityCosts: null,          // 质量成本数据需集成 SAP 后补充
            period8Ds.Count,
            closed8Ds,
            eightDClosureRate);
    }

    private static double CalculateStdDev(double[] values, double mean)
    {
        if (values.Length < 2) return 0;
        var sumSq = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSq / (values.Length - 1));
    }

    private static string GetBestCpk(QualityReportData data)
    {
        var best = data.SpcSummaries.Values
            .Where(s => s.Cpk.HasValue)
            .OrderByDescending(s => s.Cpk)
            .FirstOrDefault();
        return best?.Cpk?.ToString("F3") ?? "N/A";
    }
}
