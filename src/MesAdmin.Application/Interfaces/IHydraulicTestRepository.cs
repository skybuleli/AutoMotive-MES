using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>液压功能测试结果仓储（T2.6）</summary>
public interface IHydraulicTestRepository
{
    Task<HydraulicTestResult?> GetByIdAsync(Ulid id, CancellationToken ct = default);
    Task<List<HydraulicTestResult>> GetByEquipmentAsync(string equipmentCode, int limit = 50, CancellationToken ct = default);
    Task<List<HydraulicTestResult>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default);
    Task<HydraulicTestResult?> GetLatestByEquipmentAsync(string equipmentCode, CancellationToken ct = default);
    Task AddAsync(HydraulicTestResult result, CancellationToken ct = default);
    void Update(HydraulicTestResult result);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
