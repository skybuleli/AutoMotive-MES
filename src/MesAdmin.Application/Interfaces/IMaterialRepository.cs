using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 物料批次仓储接口（T1.12 来料扫码入库 / T1.17 物料消耗反冲）。
/// </summary>
public interface IMaterialBatchRepository
{
    Task<MaterialBatch?> GetByIdAsync(Ulid id, CancellationToken cancellationToken = default);
    Task<MaterialBatch?> GetByIdTrackedAsync(Ulid id, CancellationToken cancellationToken = default);
    Task<MaterialBatch?> GetByBatchNumberAsync(string batchNumber, CancellationToken cancellationToken = default);
    Task<List<MaterialBatch>> GetPageAsync(string? materialCode, int skip, int take, CancellationToken cancellationToken = default);
    Task<int> CountAsync(string? materialCode, CancellationToken cancellationToken = default);
    Task AddAsync(MaterialBatch batch, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 物料投料绑定仓储接口（T1.15 投料批次绑定 / T1.16 Poka-Yoke）。
/// </summary>
public interface IMaterialBindingRepository
{
    Task<MaterialBinding?> GetByIdAsync(Ulid id, CancellationToken cancellationToken = default);
    Task<List<MaterialBinding>> GetByOrderIdAsync(Ulid orderId, CancellationToken cancellationToken = default);
    Task<List<MaterialBinding>> GetByProductSerialAsync(string productSerial, CancellationToken cancellationToken = default);
    Task AddAsync(MaterialBinding binding, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
