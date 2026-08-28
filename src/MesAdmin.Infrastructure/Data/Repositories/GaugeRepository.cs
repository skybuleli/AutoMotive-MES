using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace MesAdmin.Infrastructure.Data.Repositories;

/// <summary>计量器具 + 校准记录仓储（S01）。台账量小，列表全量加载后内存过滤状态。</summary>
public sealed class GaugeRepository(MesDbContext db) : IGaugeRepository, ICalibrationRecordRepository
{
    public Task<Gauge?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.Gauges.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);

    public Task<Gauge?> GetByNumberAsync(string gaugeNumber, CancellationToken ct = default)
        => db.Gauges.AsNoTracking().FirstOrDefaultAsync(g => g.GaugeNumber == gaugeNumber, ct);

    public async Task<List<Gauge>> GetAllAsync(GaugeStatus? status = null, CancellationToken ct = default)
    {
        var gauges = await db.Gauges.AsNoTracking()
            .OrderBy(g => g.NextDueAt)
            .ToListAsync(ct);

        return status.HasValue ? gauges.Where(g => g.Status == status.Value).ToList() : gauges;
    }

    public async Task AddAsync(Gauge gauge, CancellationToken ct = default)
    {
        await db.Gauges.AddAsync(gauge, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Gauge gauge, CancellationToken ct = default)
    {
        db.Gauges.Update(gauge);
        await db.SaveChangesAsync(ct);
    }

    public Task<List<CalibrationRecord>> GetByGaugeIdAsync(Ulid gaugeId, CancellationToken ct = default)
        => db.CalibrationRecords.AsNoTracking()
            .Where(r => r.GaugeId == gaugeId)
            .OrderByDescending(r => r.CalibratedAt)
            .ToListAsync(ct);

    public async Task AddAsync(CalibrationRecord record, CancellationToken ct = default)
    {
        await db.CalibrationRecords.AddAsync(record, ct);
        await db.SaveChangesAsync(ct);
    }
}
