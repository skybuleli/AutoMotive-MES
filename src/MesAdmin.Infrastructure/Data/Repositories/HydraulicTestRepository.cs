using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace MesAdmin.Infrastructure.Data;

/// <summary>液压功能测试结果仓储（EF Core, T2.6）</summary>
public class HydraulicTestRepository(MesDbContext db) : IHydraulicTestRepository
{
    public Task<HydraulicTestResult?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.Set<HydraulicTestResult>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<List<HydraulicTestResult>> GetByEquipmentAsync(string equipmentCode, int limit = 50, CancellationToken ct = default)
        => db.Set<HydraulicTestResult>().AsNoTracking()
            .Where(r => r.EquipmentCode == equipmentCode)
            .OrderByDescending(r => r.StartedAt)
            .Take(limit)
            .ToListAsync(ct);

    public Task<List<HydraulicTestResult>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
        => db.Set<HydraulicTestResult>().AsNoTracking()
            .Where(r => r.OrderId == orderId)
            .OrderByDescending(r => r.StartedAt)
            .ToListAsync(ct);

    public Task<HydraulicTestResult?> GetLatestByEquipmentAsync(string equipmentCode, CancellationToken ct = default)
        => db.Set<HydraulicTestResult>().AsNoTracking()
            .Where(r => r.EquipmentCode == equipmentCode)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(ct);

    public Task AddAsync(HydraulicTestResult result, CancellationToken ct = default)
        => db.Set<HydraulicTestResult>().AddAsync(result, ct).AsTask();

    public void Update(HydraulicTestResult result)
        => db.Set<HydraulicTestResult>().Update(result);

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
