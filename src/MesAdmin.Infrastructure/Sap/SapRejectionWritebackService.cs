using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Sap;

/// <summary>
/// T3.16 SAP 拒单回写后台服务。
/// 轮询 SapRejectionRecords 中 WritebackStatus=Pending 的记录，
/// 调用 ISapClient.WritebackRejectionAsync 回写 SAP，
/// 更新 WritebackStatus 为 WrittenBack / Failed。
/// </summary>
public sealed class SapRejectionWritebackService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SapRejectionWritebackService> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    public SapRejectionWritebackService(
        IServiceScopeFactory scopeFactory,
        ILogger<SapRejectionWritebackService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.ZLogInformation($"SAP 拒单回写服务已启动（轮询间隔 30s）");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingRejectionsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"SAP 拒单回写轮询异常");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task ProcessPendingRejectionsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.MesDbContext>();
        var sapClient = scope.ServiceProvider.GetRequiredService<ISapClient>();

        var pending = await db.SapRejectionRecords
            .Where(r => r.WritebackStatus == RejectionWritebackStatus.Pending)
            .OrderBy(r => r.RejectedAt)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return;

        _logger.ZLogInformation($"发现 {pending.Count} 条待回写 SAP 的拒单记录");

        foreach (var rejection in pending)
        {
            if (ct.IsCancellationRequested) break;

            var result = await sapClient.WritebackRejectionAsync(rejection, ct);
            if (result.Success)
            {
                rejection.MarkWrittenBack(DateTimeOffset.UtcNow);
                _logger.ZLogInformation($"拒单回写成功：外部工单 {rejection.ExternalOrderNumber}，凭证号 {result.DocumentNumber}");
            }
            else
            {
                rejection.MarkFailed(result.ErrorMessage ?? "未知错误", DateTimeOffset.UtcNow);
                _logger.ZLogWarning($"拒单回写失败：外部工单 {rejection.ExternalOrderNumber}，错误 {result.ErrorMessage}");
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
