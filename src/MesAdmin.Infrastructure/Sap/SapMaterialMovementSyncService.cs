using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Sap;

/// <summary>
/// T3.17 SAP 物料移动凭证同步后台服务。
/// 轮询 SapInventorySyncRecords 中未同步的物料移动记录，
/// 调用 ISapClient.SendMaterialMovementAsync 推送物料移动凭证至 SAP。
/// </summary>
public sealed class SapMaterialMovementSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SapMaterialMovementSyncService> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    public SapMaterialMovementSyncService(
        IServiceScopeFactory scopeFactory,
        ILogger<SapMaterialMovementSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.ZLogInformation($"SAP 物料移动同步服务已启动（轮询间隔 30s）");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMaterialMovementsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"SAP 物料移动同步轮询异常");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task ProcessPendingMaterialMovementsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.MesDbContext>();
        var sapClient = scope.ServiceProvider.GetRequiredService<ISapClient>();

        var pending = await db.SapInventorySyncRecords
            .Where(r => !r.SapSynced)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return;

        _logger.ZLogInformation($"发现 {pending.Count} 条待同步物料移动记录");

        foreach (var record in pending)
        {
            if (ct.IsCancellationRequested) break;

            var result = await sapClient.SendMaterialMovementAsync(
                record.OrderNumber, record.MaterialCode, record.Quantity,
                record.MovementType, record.Unit, ct);

            if (result.Success)
            {
                record.MarkSynced(result.DocumentNumber ?? $"SYNC-MAT-{record.Id}");
                _logger.ZLogInformation($"物料移动同步成功：物料 {record.MaterialCode}，数量 {record.Quantity}，凭证号 {result.DocumentNumber}");
            }
            else
            {
                record.MarkError(result.ErrorMessage ?? "未知错误");
                _logger.ZLogWarning($"物料移动同步失败：物料 {record.MaterialCode}，错误 {result.ErrorMessage}");
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
