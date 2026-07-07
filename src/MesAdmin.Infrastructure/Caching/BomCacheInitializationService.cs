using MesAdmin.Application.Interfaces;
using MesAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Caching;

/// <summary>
/// BOM 缓存预热后台服务（T1.11）。
/// 应用启动时从数据库加载 BOM 数据到内存缓存，
/// 确保工单创建时的齐套检查等热路径可以从缓存直接查询。
/// </summary>
public sealed class BomCacheInitializationService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBomCache _bomCache;
    private readonly ILogger<BomCacheInitializationService> _logger;

    public BomCacheInitializationService(
        IServiceScopeFactory scopeFactory,
        IBomCache bomCache,
        ILogger<BomCacheInitializationService> logger)
    {
        _scopeFactory = scopeFactory;
        _bomCache = bomCache;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.ZLogInformation($"BOM 缓存预热开始");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();

            var boms = await db.Boms
                .AsNoTracking()
                .OrderBy(b => b.ProductCode)
                .ThenBy(b => b.Version)
                .ToListAsync(cancellationToken);

            await _bomCache.InitializeAsync(boms, cancellationToken);

            _logger.ZLogInformation($"BOM 缓存预热完成：{boms.Count} 条 BOM");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.ZLogError(ex, $"BOM 缓存预热失败，将使用数据库直查模式");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
