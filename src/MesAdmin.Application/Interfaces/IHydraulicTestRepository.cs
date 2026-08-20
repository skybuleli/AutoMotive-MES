using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>液压功能测试结果仓储（T2.6）</summary>
public interface IHydraulicTestRepository
{
    Task<HydraulicTestResult?> GetByIdAsync(Ulid id, CancellationToken ct = default);

    /// <summary>按 Id 查询（跟踪实体，用于修改场景；JSON 拥有集合 + AsNoTracking + Update 会触发 EF Core 的 __synthesizedOrdinal shadow 键异常）</summary>
    Task<HydraulicTestResult?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default);

    Task<List<HydraulicTestResult>> GetByEquipmentAsync(string equipmentCode, int limit = 50, CancellationToken ct = default);
    Task<List<HydraulicTestResult>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default);
    Task<HydraulicTestResult?> GetLatestByEquipmentAsync(string equipmentCode, CancellationToken ct = default);
    Task AddAsync(HydraulicTestResult result, CancellationToken ct = default);
    void Update(HydraulicTestResult result);
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>按完成时间范围统计合格/不合格数量（看板当日产量，索引范围查询）</summary>
    Task<(int Qualified, int Defective)> CountByCompletedPeriodAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);
}
