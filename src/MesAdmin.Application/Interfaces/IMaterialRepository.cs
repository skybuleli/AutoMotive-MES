using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 物料批次仓储接口（T1.12 来料扫码入库 / T1.17 物料消耗反冲 / T1.4 齐套检查）。
/// </summary>
public interface IMaterialBatchRepository
{
    Task<MaterialBatch?> GetByIdAsync(Ulid id, CancellationToken cancellationToken = default);
    Task<MaterialBatch?> GetByIdTrackedAsync(Ulid id, CancellationToken cancellationToken = default);
    Task<MaterialBatch?> GetByBatchNumberAsync(string batchNumber, CancellationToken cancellationToken = default);
    Task<List<MaterialBatch>> GetPageAsync(string? materialCode, int skip, int take, CancellationToken cancellationToken = default);
    /// <summary>T1.17 物料消耗反冲：返回 EF 跟踪状态的批次（支持 Consume() 持久化，避免 N+1 查询）。</summary>
    Task<List<MaterialBatch>> GetTrackedPageAsync(string materialCode, int skip, int take, CancellationToken cancellationToken = default);
    Task<int> CountAsync(string? materialCode, CancellationToken cancellationToken = default);
    Task AddAsync(MaterialBatch batch, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>T1.4 齐套：查询某物料编码的可用库存总量（所有 Qualified 批次的 RemainingQuantity 汇总）。</summary>
    Task<double> GetAvailableQuantityAsync(string materialCode, CancellationToken cancellationToken = default);

    /// <summary>T1.4 齐套：批量查询多个物料的可用库存（物料编码 → 可用量字典）。</summary>
    Task<Dictionary<string, double>> GetAvailableQuantitiesAsync(IEnumerable<string> materialCodes, CancellationToken cancellationToken = default);
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

/// <summary>
/// BOM 仓储接口（T1.4 齐套检查：按产品编码 + BOM 版本查询物料清单）。
/// </summary>
public interface IBomRepository
{
    /// <summary>查询指定产品和版本的 BOM，含所有层级物料项。</summary>
    Task<Bom?> GetByProductAndVersionAsync(string productCode, string version, CancellationToken cancellationToken = default);
}

/// <summary>
/// 物料消耗仓储接口（T1.17 物料消耗反冲）。
/// </summary>
public interface IMaterialConsumptionRepository
{
    Task AddAsync(MaterialConsumption consumption, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<MaterialConsumption> consumptions, CancellationToken cancellationToken = default);
    Task<List<MaterialConsumption>> GetByOrderIdAsync(Ulid orderId, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 消耗差异报告仓储接口（T1.17 差异 > 2%）。
/// </summary>
public interface IConsumptionVarianceRepository
{
    Task AddAsync(ConsumptionVarianceReport report, CancellationToken cancellationToken = default);
    Task<List<ConsumptionVarianceReport>> GetUnresolvedAsync(CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// SAP 库存同步记录仓储接口（T1.17 → T3.14）。
/// </summary>
public interface ISapInventorySyncRecordRepository
{
    Task AddAsync(SapInventorySyncRecord record, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<SapInventorySyncRecord> records, CancellationToken cancellationToken = default);
    Task<List<SapInventorySyncRecord>> GetPendingSyncAsync(CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// JIT 看板拉动信号仓储接口（T1.4 齐套缺料 / T1.14 JIT 看板拉动）。
/// </summary>
public interface IJitPullSignalRepository
{
    Task<JitPullSignal?> GetByIdAsync(Ulid id, CancellationToken cancellationToken = default);
    /// <summary>跟踪查询（用于 Deliver/Cancel 等写操作）。</summary>
    Task<JitPullSignal?> GetByIdTrackedAsync(Ulid id, CancellationToken cancellationToken = default);
    Task<List<JitPullSignal>> GetByOrderIdAsync(Ulid orderId, CancellationToken cancellationToken = default);
    /// <summary>跟踪查询某工单某物料的待处理信号（用于齐套检查时取消旧信号，需要 EF 跟踪以持久化 Cancel() 调用）。</summary>
    Task<List<JitPullSignal>> GetPendingByOrderAndMaterialTrackedAsync(Ulid orderId, string materialCode, CancellationToken cancellationToken = default);
    Task<List<JitPullSignal>> GetPendingAsync(CancellationToken cancellationToken = default);
    /// <summary>分页查询全部信号（含已送达/已取消）。</summary>
    Task<List<JitPullSignal>> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default);
    /// <summary>信号总数。</summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    Task AddAsync(JitPullSignal signal, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
