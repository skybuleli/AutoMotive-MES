using FluentEmail.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Reports;

/// <summary>
/// 综合管理月报定时推送服务（T4.3）。
/// 每月 1 日 06:00 自动生成综合月报 PDF 并通过邮件推送给管理层。
/// </summary>
public sealed class MonthlyBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReportEngineService _engine;
    private readonly IConfiguration _config;
    private readonly ILogger<MonthlyBackgroundService> _logger;

    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    public MonthlyBackgroundService(
        IServiceScopeFactory scopeFactory,
        ReportEngineService engine,
        IConfiguration config,
        ILogger<MonthlyBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _engine = engine;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.ZLogInformation($"综合月报定时推送服务已启动");

        await Task.Delay(StartupDelay, ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.Now;
                var emailEnabled = _config.GetValue<bool>("MonthlyReport:Email:Enabled");

                // 每月 1 日 06:00 生成并发送月报
                if (emailEnabled && now.Day == 1 && now.Hour == 6 && now.Minute == 0)
                {
                    _logger.ZLogInformation($"开始生成综合月报");
                    await GenerateAndSendMonthlyReportAsync(now, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.ZLogError(ex, $"综合月报定时生成失败");
            }

            await Task.Delay(TimeSpan.FromSeconds(60), ct);
        }
    }

    /// <summary>即时生成综合月报 PDF（API 入口）</summary>
    public async Task<byte[]> GenerateOnDemandAsync(
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        return await _engine.GenerateReportAsync("monthly", start, end, ct);
    }

    private async Task GenerateAndSendMonthlyReportAsync(
        DateTimeOffset now, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var emailFactory = scope.ServiceProvider.GetRequiredService<IFluentEmailFactory>();

        var lastMonthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset).AddMonths(-1);
        var thisMonthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);

        var pdf = await _engine.GenerateReportAsync("monthly", lastMonthStart, thisMonthStart, ct);

        var recipients = _config.GetSection("MonthlyReport:Email:Recipients").Get<string[]>();
        if (recipients is null || recipients.Length == 0)
        {
            _logger.ZLogWarning($"综合月报收件人未配置，跳过邮件发送");
            return;
        }

        var email = emailFactory.Create();
        email.To(string.Join(";", recipients));
        email.Subject($"[AutoMES] 综合管理月报 {lastMonthStart:yyyy-MM}");

        var htmlBody = $"<h2>AutoMES 博世 ESP 质量管理系统 — 综合管理月报 {lastMonthStart:yyyy-MM}</h2>"
            + "<hr/>"
            + "<p>综合管理月报已生成，详情请查看附件 PDF。</p>"
            + $"<p style='color: #888; font-size: 0.8em;'>生成时间：{DateTimeOffset.UtcNow.ToLocalTime():yyyy-MM-dd HH:mm}</p>"
            + "<hr/>"
            + "<ul>"
            + "<li><b>内容包括：</b>产量完成率、合格率/FPY/PPM、Cpk 趋势、质量成本、OEE 月度趋势、8D 闭环率、Andon/维护统计</li>"
            + "</ul>"
            + "<hr/>"
            + "<p style='color: #888; font-size: 0.8em;'>AutoMES 自动生成 · 博世 ESP 制动系统质量管理系统</p>";

        email.Body(htmlBody, isHtml: true);
        email.Attach(new FluentEmail.Core.Models.Attachment
        {
            Filename = $"AutoMES_Monthly_{lastMonthStart:yyyyMM}.pdf",
            Data = new MemoryStream(pdf),
            ContentType = "application/pdf"
        });

        await email.SendAsync(ct);
        _logger.ZLogInformation($"综合月报邮件已发送至 {string.Join(", ", recipients)}");
    }
}
