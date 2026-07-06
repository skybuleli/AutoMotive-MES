using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MesAdmin.Infrastructure.Data.Repositories;

/// <summary>
/// Andon 报警事件 EF Core 仓储实现（T2.20）。
/// 自动扫描注册（Namespace MesAdmin.Infrastructure.Data → source generator）。
/// </summary>
public class AndonEventRepository(MesDbContext db) : IAndonEventRepository
{
    public Task<AndonEvent?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.AndonEvents.FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<List<AndonEvent>> GetListAsync(
        AndonEventStatus? status = null,
        string? equipmentCode = null,
        AndonSeverity? severity = null,
        int limit = 50,
        CancellationToken ct = default)
    {
        var query = db.AndonEvents.AsQueryable();

        if (status.HasValue)
            query = query.Where(e => e.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(equipmentCode))
            query = query.Where(e => e.EquipmentCode == equipmentCode);
        if (severity.HasValue)
            query = query.Where(e => e.Severity == severity.Value);

        return query
            .OrderByDescending(e => e.OccurredAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public Task<List<AndonEvent>> GetActiveAsync(CancellationToken ct = default)
        => db.AndonEvents
            .Where(e => e.Status != AndonEventStatus.Closed)
            .OrderByDescending(e => e.OccurredAt)
            .ToListAsync(ct);

    public Task<int> GetActiveCountAsync(CancellationToken ct = default)
        => db.AndonEvents
            .CountAsync(e => e.Status != AndonEventStatus.Closed
                          && e.Status != AndonEventStatus.Resolved, ct);

    public Task AddAsync(AndonEvent ev, CancellationToken ct = default)
    {
        db.AndonEvents.Add(ev);
        return db.SaveChangesAsync(ct);
    }

    public Task UpdateAsync(AndonEvent ev, CancellationToken ct = default)
    {
        db.AndonEvents.Update(ev);
        return db.SaveChangesAsync(ct);
    }

    public async Task UpdateRangeAsync(List<AndonEvent> events, CancellationToken ct = default)
    {
        db.AndonEvents.UpdateRange(events);
        await db.SaveChangesAsync(ct);
    }
}
