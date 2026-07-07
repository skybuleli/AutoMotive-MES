using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Sap;

/// <summary>
/// T3.14 SAP 工单双向同步后台服务。
/// 轮询 SapOrderSyncRecords 中未同步的记录，调用 ISapClient.SendOrderStatusAsync 推送至 SAP。
/// </summary>
public sealed class SapOrderSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SapOrderSyncService> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    public SapOrderSyncService(
        IServiceScopeFactory scopeFactory,
        ILogger<SapOrderSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.ZLogInformation($"SAP 工单同步服务已启动（轮询间隔 30s）");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingOrderSyncsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"SAP 工单同步轮询异常");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task ProcessPendingOrderSyncsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.MesDbContext>();
        var sapClient = scope.ServiceProvider.GetRequiredService<ISapClient>();

        var pending = await db.SapOrderSyncRecords
            .Where(r => !r.SapSynced)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return;

        _logger.ZLogInformation($"发现 {pending.Count} 条待同步工单状态记录");

        foreach (var record in pending)
        {
            if (ct.IsCancellationRequested) break;

            var result = await sapClient.SendOrderStatusAsync(
                record.ExternalOrderNumber, record.Status, record.QualifiedQuantity, ct);

            if (result.Success)
            {
                record.MarkSynced(result.DocumentNumber ?? $"SYNC-{record.Id}");
                _logger.ZLogInformation($"工单状态同步成功：外部工单 {record.ExternalOrderNumber}，状态 {record.Status}，凭证号 {result.DocumentNumber}");
            }
            else
            {
                record.MarkError(result.ErrorMessage ?? "未知错误");
                _logger.ZLogWarning($"工单状态同步失败：外部工单 {record.ExternalOrderNumber}，错误 {result.ErrorMessage}");
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
