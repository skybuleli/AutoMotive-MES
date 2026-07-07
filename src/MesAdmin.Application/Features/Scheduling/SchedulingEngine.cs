using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.Scheduling;

/// <summary>
/// 有限产能排程引擎（M09 T3.11-T3.12）。
/// 支持最早可排时间计算、设备冲突检测、紧急插单。
/// </summary>
public class SchedulingEngine
{
    private readonly IScheduleRepository _scheduleRepo;
    private readonly ICapacityCalendarRepository _calendarRepo;

    public SchedulingEngine(
        IScheduleRepository scheduleRepo,
        ICapacityCalendarRepository calendarRepo)
    {
        _scheduleRepo = scheduleRepo;
        _calendarRepo = calendarRepo;
    }

    /// <summary>
    /// 计算指定设备在指定日期范围内的最早可排时间。
    /// 考虑已有排程占用的时间段 + 换型时间。
    /// </summary>
    public async Task<DateTimeOffset> GetEarliestAvailableSlotAsync(
        string equipmentCode,
        DateOnly date,
        double requiredMinutes,
        double changeoverMinutes,
        string? currentProductCode = null,
        string? newProductCode = null,
        CancellationToken ct = default)
    {
        var existingSchedules = await _scheduleRepo.GetByEquipmentAsync(equipmentCode, date, date, ct);
        var calendar = await _calendarRepo.GetByEquipmentAsync(equipmentCode, ct);

        // 获取该天的班次可用时间
        var shiftStart = GetShiftStartTime(date, ShiftType.Morning);
        var shiftEnd = GetShiftEndTime(date, ShiftType.Morning);

        // 如果有晚班，扩大范围
        var morningEnd = shiftStart.AddHours(8);
        var afternoonStart = shiftStart.AddHours(8);
        var afternoonEnd = afternoonStart.AddHours(8);

        // 按开始时间排序已有的排程
        var ordered = existingSchedules
            .Where(s => s.Status != ScheduleStatus.Cancelled)
            .OrderBy(s => s.PlannedStartAt)
            .ToList();

        // 计算实际的换型时间
        var effectiveChangeover = GetEffectiveChangeoverMinutes(calendar, currentProductCode, newProductCode);

        // 最早可排时间从班次开始
        var cursor = shiftStart;

        // 遍历已有排程，找到第一个可用间隙
        foreach (var schedule in ordered)
        {
            // 如果有间隙足够容纳当前排程（含换型时间）
            var gapMinutes = (schedule.PlannedStartAt - cursor).TotalMinutes;
            if (gapMinutes >= requiredMinutes + effectiveChangeover)
            {
                return cursor.AddMinutes(effectiveChangeover);
            }

            // 跳过当前排程占用的时间段
            cursor = schedule.PlannedEndAt > cursor ? schedule.PlannedEndAt : cursor;
        }

        // 检查最后一个排程之后是否有足够时间
        var remainingMinutes = (afternoonEnd - cursor).TotalMinutes;
        if (remainingMinutes >= requiredMinutes + effectiveChangeover)
        {
            return cursor.AddMinutes(effectiveChangeover);
        }

        // 当天无可用时间段，返回下个班次开始
        return GetNextShiftStart(date);
    }

    /// <summary>
    /// 检测设备排程冲突。
    /// 验证新排程不与任何已有排程的时间重叠。
    /// </summary>
    public async Task<List<ScheduleConflict>> DetectConflictsAsync(
        string equipmentCode,
        DateTimeOffset plannedStart,
        DateTimeOffset plannedEnd,
        Ulid? excludeScheduleId = null,
        CancellationToken ct = default)
    {
        var conflicts = new List<ScheduleConflict>();
        var date = DateOnly.FromDateTime(plannedStart.DateTime);

        var existingSchedules = await _scheduleRepo.GetByEquipmentAsync(equipmentCode, date, date, ct);

        foreach (var schedule in existingSchedules)
        {
            // 排除自己（更新时）
            if (excludeScheduleId.HasValue && schedule.Id == excludeScheduleId.Value)
                continue;

            if (schedule.Status == ScheduleStatus.Cancelled)
                continue;

            // 检测时间重叠
            if (plannedStart < schedule.PlannedEndAt && plannedEnd > schedule.PlannedStartAt)
            {
                conflicts.Add(new ScheduleConflict
                {
                    Description = $"设备 {equipmentCode} 与排程 {schedule.OrderNumber} 的时间冲突",
                    Severity = "Error",
                    EquipmentCode = equipmentCode,
                    StartAt = schedule.PlannedStartAt,
                    EndAt = schedule.PlannedEndAt,
                    ConflictingScheduleId = schedule.Id.ToString(),
                });
            }
        }

        return conflicts;
    }

    /// <summary>
    /// 紧急插单（T3.12）。
    /// 将急单插入排程，自动调整受影响排程的时间。
    /// 返回插入后的排程建议和冲突警告。
    /// </summary>
    public async Task<(ProductionSchedule Schedule, List<ScheduleConflict> Conflicts)> InsertRushOrderAsync(
        Ulid orderId,
        string orderNumber,
        string productCode,
        int plannedQuantity,
        string equipmentCode,
        int station,
        double standardMinutes,
        double changeoverMinutes,
        RushOrderType rushType,
        string? rushReason = null,
        CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);

        // 紧急插单：尝试插入当天最早可用时段
        var earliestStart = await GetEarliestAvailableSlotAsync(
            equipmentCode, today, standardMinutes, changeoverMinutes,
            null, productCode, ct);

        // 检测冲突
        var plannedEnd = earliestStart.AddMinutes(standardMinutes + changeoverMinutes);
        var conflicts = await DetectConflictsAsync(equipmentCode, earliestStart, plannedEnd, null, ct);

        // 如果有严重冲突，尝试推后
        if (conflicts.Any(c => c.Severity == "Error"))
        {
            // 找到冲突结束后再排
            var maxConflictEnd = conflicts.Max(c => c.EndAt);
            earliestStart = maxConflictEnd;
            plannedEnd = earliestStart.AddMinutes(standardMinutes + changeoverMinutes);

            // 重新检测（排除旧冲突）
            conflicts = await DetectConflictsAsync(equipmentCode, earliestStart, plannedEnd, null, ct);
        }

        var schedule = ProductionSchedule.Create(
            Ulid.NewUlid(),
            orderId,
            orderNumber,
            productCode,
            plannedQuantity,
            equipmentCode,
            station,
            ShiftType.Morning,
            DateOnly.FromDateTime(earliestStart.DateTime),
            earliestStart,
            standardMinutes,
            changeoverMinutes,
            priority: 3, // 插单最高优先级
            rushType: rushType,
            rushReason: rushReason);

        return (schedule, conflicts);
    }

    /// <summary>
    /// 批量计算设备的产能利用率。
    /// </summary>
    public async Task<Dictionary<string, double>> CalculateUtilizationAsync(
        DateOnly date,
        CancellationToken ct = default)
    {
        var calendars = await _calendarRepo.GetAllActiveAsync(ct);
        var result = new Dictionary<string, double>();

        var shiftMinutes = 8 * 60; // 每个班次 8 小时

        foreach (var cal in calendars)
        {
            var schedules = await _scheduleRepo.GetByEquipmentAsync(cal.EquipmentCode, date, date, ct);
            var activeMinutes = schedules
                .Where(s => s.Status == ScheduleStatus.Scheduled || s.Status == ScheduleStatus.InProgress)
                .Sum(s => s.StandardMinutes + s.ChangeoverMinutes);

            var utilization = shiftMinutes > 0
                ? Math.Round(Math.Min(activeMinutes / (double)shiftMinutes, 1.0) * 100, 1)
                : 0;

            result[cal.EquipmentCode] = utilization;
        }

        return result;
    }

    // ═══════════════════════════════════════════
    //  辅助方法
    // ═══════════════════════════════════════════

    private static DateTimeOffset GetShiftStartTime(DateOnly date, ShiftType shift)
    {
        var baseDate = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return shift switch
        {
            ShiftType.Morning => new DateTimeOffset(baseDate.AddHours(6), TimeSpan.Zero),
            ShiftType.Afternoon => new DateTimeOffset(baseDate.AddHours(14), TimeSpan.Zero),
            ShiftType.Night => new DateTimeOffset(baseDate.AddHours(22), TimeSpan.Zero),
            _ => new DateTimeOffset(baseDate.AddHours(6), TimeSpan.Zero),
        };
    }

    private static DateTimeOffset GetShiftEndTime(DateOnly date, ShiftType shift)
    {
        var baseDate = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return shift switch
        {
            ShiftType.Morning => new DateTimeOffset(baseDate.AddHours(14), TimeSpan.Zero),
            ShiftType.Afternoon => new DateTimeOffset(baseDate.AddHours(22), TimeSpan.Zero),
            ShiftType.Night => new DateTimeOffset(baseDate.AddDays(1).AddHours(6), TimeSpan.Zero),
            _ => new DateTimeOffset(baseDate.AddHours(14), TimeSpan.Zero),
        };
    }

    private static DateTimeOffset GetNextShiftStart(DateOnly date)
    {
        var baseDate = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return new DateTimeOffset(baseDate.AddDays(1).AddHours(6), TimeSpan.Zero);
    }

    private static double GetEffectiveChangeoverMinutes(
        CapacityCalendar? calendar,
        string? currentProductCode,
        string? newProductCode)
    {
        if (calendar is null)
            return 15; // 默认 15 分钟

        // 同产品换型 = 标准换型时间
        if (string.IsNullOrEmpty(currentProductCode) || string.IsNullOrEmpty(newProductCode))
            return calendar.StandardChangeoverMinutes;

        // 跨产品换型 = 更长的换型时间
        return !string.Equals(currentProductCode, newProductCode, StringComparison.OrdinalIgnoreCase)
            ? calendar.CrossProductChangeoverMinutes
            : calendar.StandardChangeoverMinutes;
    }
}
