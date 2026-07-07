using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 生产排程仓储接口（M09 T3.10）。
/// </summary>
public interface IScheduleRepository
{
    Task<ProductionSchedule?> GetByIdAsync(Ulid id, CancellationToken ct = default);
    Task<List<ProductionSchedule>> GetByDateAsync(DateOnly date, CancellationToken ct = default);
    Task<List<ProductionSchedule>> GetByDateRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<List<ProductionSchedule>> GetByEquipmentAsync(string equipmentCode, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default);
    Task<List<ProductionSchedule>> GetByStatusAsync(ScheduleStatus status, CancellationToken ct = default);
    Task<List<ProductionSchedule>> GetRushOrdersAsync(CancellationToken ct = default);
    Task<List<ProductionSchedule>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default);
    Task AddAsync(ProductionSchedule schedule, CancellationToken ct = default);
    void Update(ProductionSchedule schedule);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// 产能日历仓储接口（M09 T3.10）。
/// </summary>
public interface ICapacityCalendarRepository
{
    Task<CapacityCalendar?> GetByIdAsync(Ulid id, CancellationToken ct = default);
    Task<List<CapacityCalendar>> GetAllActiveAsync(CancellationToken ct = default);
    Task<CapacityCalendar?> GetByEquipmentAsync(string equipmentCode, CancellationToken ct = default);
    Task AddAsync(CapacityCalendar calendar, CancellationToken ct = default);
    void Update(CapacityCalendar calendar);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
