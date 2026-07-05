using Microsoft.EntityFrameworkCore;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.Data.Repositories;

/// <summary>
/// 物料批次仓储实现（委托同一 Scoped MesDbContext）。
/// </summary>
public class MaterialBatchRepository(MesDbContext db) : IMaterialBatchRepository
{
    public Task<MaterialBatch?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.MaterialBatches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<MaterialBatch?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default)
        => db.MaterialBatches
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<MaterialBatch?> GetByBatchNumberAsync(string batchNumber, CancellationToken ct = default)
        => db.MaterialBatches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.BatchNumber == batchNumber, ct);

    public Task<List<MaterialBatch>> GetPageAsync(string? materialCode, int skip, int take, CancellationToken ct = default)
    {
        var query = db.MaterialBatches.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(materialCode))
            query = query.Where(b => b.MaterialCode == materialCode);
        return query
            .OrderByDescending(b => b.ReceivedAt)
            .ThenByDescending(b => b.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    /// <summary>T1.17 物料消耗反冲：返回 EF 跟踪状态的批次（支持 Consume() 持久化，避免 N+1 查询）。</summary>
    public Task<List<MaterialBatch>> GetTrackedPageAsync(string materialCode, int skip, int take, CancellationToken ct = default)
        => db.MaterialBatches
            .Where(b => b.MaterialCode == materialCode)
            .OrderByDescending(b => b.ReceivedAt)
            .ThenByDescending(b => b.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public Task<int> CountAsync(string? materialCode, CancellationToken ct = default)
    {
        var query = db.MaterialBatches.AsQueryable();
        if (!string.IsNullOrWhiteSpace(materialCode))
            query = query.Where(b => b.MaterialCode == materialCode);
        return query.CountAsync(ct);
    }

    public Task AddAsync(MaterialBatch batch, CancellationToken ct = default)
        => db.MaterialBatches.AddAsync(batch, ct).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);

    /// <summary>T1.4 齐套：查询某物料编码的可用库存总量（所有 Qualified 批次的 RemainingQuantity 汇总）。</summary>
    public Task<double> GetAvailableQuantityAsync(string materialCode, CancellationToken ct = default)
        => db.MaterialBatches
            .Where(b => b.MaterialCode == materialCode && b.Status == MaterialBatchStatus.Qualified)
            .SumAsync(b => b.RemainingQuantity, ct);

    /// <summary>T1.4 齐套：批量查询多个物料的可用库存（物料编码 → 可用量字典）。</summary>
    public async Task<Dictionary<string, double>> GetAvailableQuantitiesAsync(
        IEnumerable<string> materialCodes, CancellationToken ct = default)
    {
        var codes = materialCodes.Distinct().ToList();
        if (codes.Count == 0)
            return [];

        var results = await db.MaterialBatches
            .Where(b => codes.Contains(b.MaterialCode) && b.Status == MaterialBatchStatus.Qualified)
            .GroupBy(b => b.MaterialCode)
            .Select(g => new { MaterialCode = g.Key, Total = g.Sum(b => b.RemainingQuantity) })
            .ToListAsync(ct);

        // 确保每个查询的物料编码都有条目（未在库的返回 0）
        var dict = results.ToDictionary(r => r.MaterialCode, r => r.Total);
        foreach (var code in codes)
        {
            if (!dict.ContainsKey(code))
                dict[code] = 0;
        }
        return dict;
    }
}

/// <summary>
/// 物料投料绑定仓储实现。
/// </summary>
public class MaterialBindingRepository(MesDbContext db) : IMaterialBindingRepository
{
    public Task<MaterialBinding?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.MaterialBindings
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<List<MaterialBinding>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
        => db.MaterialBindings
            .AsNoTracking()
            .Where(b => b.OrderId == orderId)
            .OrderBy(b => b.BoundAt)
            .ToListAsync(ct);

    public Task<List<MaterialBinding>> GetByProductSerialAsync(string productSerial, CancellationToken ct = default)
        => db.MaterialBindings
            .AsNoTracking()
            .Where(b => b.ProductSerial == productSerial)
            .OrderBy(b => b.BoundAt)
            .ToListAsync(ct);

    public Task AddAsync(MaterialBinding binding, CancellationToken ct = default)
        => db.MaterialBindings.AddAsync(binding, ct).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}

/// <summary>
/// JIT 看板拉动信号仓储实现。
/// </summary>
public class JitPullSignalRepository(MesDbContext db) : IJitPullSignalRepository
{
    public Task<JitPullSignal?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.JitPullSignals
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<JitPullSignal?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default)
        => db.JitPullSignals
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<List<JitPullSignal>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
        => db.JitPullSignals
            .AsNoTracking()
            .Where(s => s.OrderId == orderId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

    /// <summary>跟踪查询某工单某物料的待处理信号（EF 跟踪，可持久化 Cancel() 调用）。</summary>
    public Task<List<JitPullSignal>> GetPendingByOrderAndMaterialTrackedAsync(
        Ulid orderId, string materialCode, CancellationToken ct = default)
        => db.JitPullSignals
            .Where(s => s.OrderId == orderId && s.MaterialCode == materialCode && s.Status == JitPullStatus.Created)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

    public Task<List<JitPullSignal>> GetPendingAsync(CancellationToken ct = default)
        => db.JitPullSignals
            .AsNoTracking()
            .Where(s => s.Status == JitPullStatus.Created)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

    public Task<List<JitPullSignal>> GetPageAsync(int skip, int take, CancellationToken ct = default)
        => db.JitPullSignals
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public Task<int> CountAsync(CancellationToken ct = default)
        => db.JitPullSignals
            .CountAsync(ct);

    public Task AddAsync(JitPullSignal signal, CancellationToken ct = default)
        => db.JitPullSignals.AddAsync(signal, ct).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
