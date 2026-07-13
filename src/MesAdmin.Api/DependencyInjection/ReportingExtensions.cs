using System.Net.Mail;
using FluentEmail.Smtp;
using MesAdmin.Infrastructure.Reports;
using QuestPDF.Infrastructure;

namespace MesAdmin.Api.DependencyInjection;

/// <summary>
/// 报表引擎与邮件推送 DI 注册扩展。
/// </summary>
public static class ReportingExtensions
{
    public static IServiceCollection AddMesReporting(this IServiceCollection services, IConfiguration configuration)
    {
        // ── QuestPDF 社区许可 ──
        QuestPDF.Settings.License = LicenseType.Community;

        // ── FluentEmail SMTP（质量报表邮件推送）──
        services.AddFluentEmail(configuration["QualityReports:Email:From"] ?? "automes@bosch.com")
            .AddSmtpSender(new SmtpClient
            {
                Host = configuration["QualityReports:Email:SmtpHost"] ?? "localhost",
                Port = configuration.GetValue<int>("QualityReports:Email:SmtpPort", 25),
                EnableSsl = configuration.GetValue<bool>("QualityReports:Email:EnableSsl"),
                Credentials = !string.IsNullOrEmpty(configuration["QualityReports:Email:Username"])
                    ? new System.Net.NetworkCredential(
                        configuration["QualityReports:Email:Username"],
                        configuration["QualityReports:Email:Password"])
                    : null
            });

        // ── 质量报表服务 ──
        services.AddSingleton<PdfReportGenerator>();
        services.AddSingleton<QualityReportService>();
        services.AddHostedService<QualityReportService>(sp => sp.GetRequiredService<QualityReportService>());

        // ── 报表引擎服务 ──
        services.AddSingleton<OeeReportStore>();
        services.AddSingleton<ReportDataSourceService>();
        services.AddSingleton<ReportEngineService>();

        // ── OEE 日报定时推送 ──
        services.AddSingleton<OeeDailyBackgroundService>();
        services.AddHostedService<OeeDailyBackgroundService>(sp => sp.GetRequiredService<OeeDailyBackgroundService>());

        // ── 综合月报定时推送 ──
        services.AddSingleton<MonthlyBackgroundService>();
        services.AddHostedService<MonthlyBackgroundService>(sp => sp.GetRequiredService<MonthlyBackgroundService>());

        return services;
    }
}
