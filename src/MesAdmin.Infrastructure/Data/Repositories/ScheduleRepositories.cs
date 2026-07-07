using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace MesAdmin.Infrastructure.Data.Repositories;

/// <summary>生产排程仓储</summary>
public class ScheduleRepository(MesDbContext db) : IScheduleRepository
{
    public Task<ProductionSchedule?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.ProductionSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<List<ProductionSchedule>> GetByDateAsync(DateOnly date, CancellationToken ct = default)
        => db.ProductionSchedules.AsNoTracking()
            .Where(s => s.ScheduleDate == date)
            .OrderBy(s => s.Priority).ThenBy(s => s.PlannedStartAt)
            .ToListAsync(ct);

    public Task<List<ProductionSchedule>> GetByDateRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        => db.ProductionSchedules.AsNoTracking()
            .Where(s => s.ScheduleDate >= from && s.ScheduleDate <= to)
            .OrderBy(s => s.ScheduleDate).ThenBy(s => s.Priority).ThenBy(s => s.PlannedStartAt)
            .ToListAsync(ct);

    public Task<List<ProductionSchedule>> GetByEquipmentAsync(string equipmentCode, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        var query = db.ProductionSchedules.AsNoTracking()
            .Where(s => s.EquipmentCode == equipmentCode);

        if (from.HasValue)
            query = query.Where(s => s.ScheduleDate >= from.Value);
        if (to.HasValue)
            query = query.Where(s => s.ScheduleDate <= to.Value);

        return query.OrderBy(s => s.PlannedStartAt).ToListAsync(ct);
    }

    public Task<List<ProductionSchedule>> GetByStatusAsync(ScheduleStatus status, CancellationToken ct = default)
        => db.ProductionSchedules.AsNoTracking()
            .Where(s => s.Status == status)
            .OrderBy(s => s.PlannedStartAt)
            .ToListAsync(ct);

    public Task<List<ProductionSchedule>> GetRushOrdersAsync(CancellationToken ct = default)
        => db.ProductionSchedules.AsNoTracking()
            .Where(s => s.RushType != RushOrderType.None && s.Status == ScheduleStatus.Scheduled)
            .OrderBy(s => s.Priority).ThenBy(s => s.PlannedStartAt)
            .ToListAsync(ct);

    public Task<List<ProductionSchedule>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
        => db.ProductionSchedules.AsNoTracking()
            .Where(s => s.OrderId == orderId)
            .OrderBy(s => s.PlannedStartAt)
            .ToListAsync(ct);

    public Task AddAsync(ProductionSchedule schedule, CancellationToken ct = default)
        => db.ProductionSchedules.AddAsync(schedule, ct).AsTask();

    public void Update(ProductionSchedule schedule) => db.ProductionSchedules.Update(schedule);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

/// <summary>产能日历仓储</summary>
public class CapacityCalendarRepository(MesDbContext db) : ICapacityCalendarRepository
{
    public Task<CapacityCalendar?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.CapacityCalendars.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<List<CapacityCalendar>> GetAllActiveAsync(CancellationToken ct = default)
        => db.CapacityCalendars.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.EquipmentCode)
            .ToListAsync(ct);

    public Task<CapacityCalendar?> GetByEquipmentAsync(string equipmentCode, CancellationToken ct = default)
        => db.CapacityCalendars.AsNoTracking()
            .FirstOrDefaultAsync(c => c.EquipmentCode == equipmentCode, ct);

    public Task AddAsync(CapacityCalendar calendar, CancellationToken ct = default)
        => db.CapacityCalendars.AddAsync(calendar, ct).AsTask();

    public void Update(CapacityCalendar calendar) => db.CapacityCalendars.Update(calendar);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
