using Microsoft.EntityFrameworkCore;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.Data.Repositories;

/// <summary>
/// BOM 仓储实现（T1.4 齐套检查 / T1.11 缓存优化）。
/// 缓存优先策略：先查 BomCache 内存缓存，未命中时回退到 EF Core 数据库查询。
/// 缓存由 BomCacheInitializationService 在启动时预加载。
/// </summary>
public class BomRepository(MesDbContext db, IBomCache bomCache) : IBomRepository
{
    public async Task<Bom?> GetByProductAndVersionAsync(string productCode, string version, CancellationToken ct = default)
    {
        // T1.11 缓存优先：内存命中则直接返回，避免数据库查询
        if (bomCache.IsInitialized)
        {
            var cached = await bomCache.GetByProductAndVersionAsync(productCode, version, ct);
            if (cached is not null)
                return cached;
        }

        // 缓存未命中 / 未初始化：回退到 EF Core 查询
        return await db.Boms
            .AsNoTracking()
            .Where(b => b.ProductCode == productCode && b.Version == version)
            .OrderByDescending(b => b.EffectiveDate)
            .FirstOrDefaultAsync(ct);
    }
}
