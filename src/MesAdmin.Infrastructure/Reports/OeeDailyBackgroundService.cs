using FluentEmail.Core;
using MesAdmin.Application.Features.Reports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Reports;

/// <summary>
/// OEE 日报定时推送服务（T4.2）。
/// 每日 06:00 自动生成 OEE 日报 PDF 并通过邮件推送给生产经理和设备工程师。
/// 依赖 ReportEngineService（模板引擎）和 FluentEmail。
/// </summary>
public sealed class OeeDailyBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReportEngineService _engine;
    private readonly IConfiguration _config;
    private readonly ILogger<OeeDailyBackgroundService> _logger;

    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    public OeeDailyBackgroundService(
        IServiceScopeFactory scopeFactory,
        ReportEngineService engine,
        IConfiguration config,
        ILogger<OeeDailyBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _engine = engine;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.ZLogInformation($"OEE 日报定时推送服务已启动");

        await Task.Delay(StartupDelay, ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.Now;
                var emailEnabled = _config.GetValue<bool>("OeeReports:Email:Enabled");

                if (emailEnabled && now.Hour == 6 && now.Minute == 0)
                {
                    _logger.ZLogInformation($"开始生成 OEE 日报");
                    await GenerateAndSendDailyReportAsync(now, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.ZLogError(ex, $"OEE 日报定时生成失败");
            }

            await Task.Delay(TimeSpan.FromSeconds(60), ct);
        }
    }

    /// <summary>即时生成 OEE 日报 PDF（API 调用入口）</summary>
    public async Task<byte[]> GenerateOnDemandAsync(
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        return await _engine.GenerateReportAsync("oee-daily", start, end, ct);
    }

    private async Task GenerateAndSendDailyReportAsync(
        DateTimeOffset now, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var emailFactory = scope.ServiceProvider.GetRequiredService<IFluentEmailFactory>();

        var yesterday = now.Date.AddDays(-1);
        var today = now.Date;

        var pdf = await _engine.GenerateReportAsync("oee-daily", yesterday, today, ct);

        var recipients = _config.GetSection("OeeReports:Email:Recipients").Get<string[]>();
        if (recipients is null || recipients.Length == 0)
        {
            _logger.ZLogWarning($"OEE 日报收件人未配置，跳过邮件发送");
            return;
        }

        var email = emailFactory.Create();
        email.To(string.Join(";", recipients));
        email.Subject($"[AutoMES] OEE 日报 {yesterday:yyyy-MM-dd}");

        var htmlBody = $"<h2>AutoMES 博世 ESP 质量管理系统 — OEE 日报 {yesterday:yyyy-MM-dd}</h2>"
            + "<hr/>"
            + "<p>OEE 日报已生成，详情请查看附件 PDF。</p>"
            + $"<p style='color: #888; font-size: 0.8em;'>生成时间：{DateTimeOffset.UtcNow.ToLocalTime():yyyy-MM-dd HH:mm}</p>"
            + "<hr/>"
            + "<p style='color: #888; font-size: 0.8em;'>AutoMES 自动生成 · 博世 ESP 制动系统质量管理系统</p>";

        email.Body(htmlBody, isHtml: true);
        email.Attach(new FluentEmail.Core.Models.Attachment
        {
            Filename = $"AutoMES_OEE_Daily_{yesterday:yyyyMMdd}.pdf",
            Data = new MemoryStream(pdf),
            ContentType = "application/pdf"
        });

        await email.SendAsync(ct);
        _logger.ZLogInformation($"OEE 日报邮件已发送至 {string.Join(", ", recipients)}");
    }
}
