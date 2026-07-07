using System.Collections.Concurrent;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using ZLogger;

namespace MesAdmin.Infrastructure.Caching;

/// <summary>
/// BOM 内存缓存（ConcurrentDictionary 实现）。
/// 键为 "{ProductCode}:{Version}" 的组合键，值包含完整的 BOM 及其物料项列表。
/// 缓存由 BomCacheInitializationService 在应用启动时预加载，
/// 支持运行时刷新（InvalidateAsync）。
/// </summary>
public sealed class BomCache : IBomCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BomCache> _logger;
    private volatile ConcurrentDictionary<string, Bom> _cache = new();
    private volatile bool _isInitialized;

    public BomCache(
        IServiceScopeFactory scopeFactory,
        ILogger<BomCache> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public bool IsInitialized => _isInitialized;

    public Task<Bom?> GetByProductAndVersionAsync(
        string productCode, string version, CancellationToken ct = default)
    {
        var key = BuildKey(productCode, version);
        if (_cache.TryGetValue(key, out var bom))
        {
            return Task.FromResult<Bom?>(bom);
        }

        return Task.FromResult<Bom?>(null);
    }

    public Task<List<Bom>> GetAllAsync(CancellationToken ct = default)
    {
        var all = _cache.Values.ToList();
        return Task.FromResult(all);
    }

    /// <summary>
    /// 从数据库重新加载所有 BOM 数据到缓存。
    /// 原子替换：创建新字典后一次性替换引用，避免查询期间的并发问题。
    /// </summary>
    public async Task InvalidateAsync(CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();

            var boms = await db.Boms
                .AsNoTracking()
                .OrderBy(b => b.ProductCode)
                .ThenBy(b => b.Version)
                .ToListAsync(ct);

            var newCache = new ConcurrentDictionary<string, Bom>(
                boms.Select(b => new KeyValuePair<string, Bom>(BuildKey(b.ProductCode, b.Version), b)));

            Interlocked.Exchange(ref _cache, newCache);
            _isInitialized = true;

            var totalItems = boms.Sum(b => b.Items.Count);
            _logger.ZLogInformation($"BOM 缓存已刷新：加载 {boms.Count} 条 BOM 记录，含 {totalItems} 个物料项");
        }
        catch (Exception ex)
        {
            _logger.ZLogError(ex, $"BOM 缓存刷新失败");
            throw;
        }
    }

    /// <summary>
    /// 使用指定的 BOM 列表初始化缓存（IBomCache.InitializeAsync 实现）。
    /// 原子替换：创建新字典后一次性替换引用，避免查询期间的并发问题。
    /// </summary>
    public Task InitializeAsync(IReadOnlyList<Bom> boms, CancellationToken ct = default)
    {
        var newCache = new ConcurrentDictionary<string, Bom>(
            boms.Select(b => new KeyValuePair<string, Bom>(BuildKey(b.ProductCode, b.Version), b)));
        Interlocked.Exchange(ref _cache, newCache);
        _isInitialized = true;

        var totalItems = boms.Sum(b => b.Items.Count);
        _logger.ZLogInformation($"BOM 缓存已初始化：加载 {boms.Count} 条 BOM 记录，含 {totalItems} 个物料项");
        return Task.CompletedTask;
    }

    private static string BuildKey(string productCode, string version)
        => $"{productCode.Trim().ToUpperInvariant()}:{version.Trim()}";
}
