using Microsoft.EntityFrameworkCore;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.Data.Repositories;

/// <summary>
/// BOM 仓储实现（T1.4 齐套检查：按产品编码 + 版本查询物料清单）。
/// </summary>
public class BomRepository(MesDbContext db) : IBomRepository
{
    public Task<Bom?> GetByProductAndVersionAsync(string productCode, string version, CancellationToken ct = default)
        => db.Boms
            .AsNoTracking()
            .Where(b => b.ProductCode == productCode && b.Version == version)
            .OrderByDescending(b => b.EffectiveDate)
            .FirstOrDefaultAsync(ct);
}
