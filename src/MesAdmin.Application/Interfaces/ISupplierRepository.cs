using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 供应商仓储接口（T3.6 M08 SQE）。
/// </summary>
public interface ISupplierRepository
{
    Task<Supplier?> GetByIdAsync(Ulid id, CancellationToken ct = default);
    Task<List<Supplier>> GetAllAsync(CancellationToken ct = default);
    Task<List<Supplier>> GetByMaterialCategoryAsync(string category, CancellationToken ct = default);
    Task<List<Supplier>> GetCriticalAsync(CancellationToken ct = default);
    Task<Supplier?> GetByCodeAsync(string supplierCode, CancellationToken ct = default);
    Task AddAsync(Supplier supplier, CancellationToken ct = default);
    void Update(Supplier supplier);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// 供应商评分卡仓储接口（T3.6）。
/// </summary>
public interface ISupplierScoreCardRepository
{
    Task<SupplierScoreCard?> GetByIdAsync(Ulid id, CancellationToken ct = default);
    Task<List<SupplierScoreCard>> GetBySupplierAsync(Ulid supplierId, CancellationToken ct = default);
    Task<List<SupplierScoreCard>> GetByPeriodAsync(string period, CancellationToken ct = default);
    Task<SupplierScoreCard?> GetLatestBySupplierAsync(Ulid supplierId, CancellationToken ct = default);
    Task AddAsync(SupplierScoreCard card, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// PPAP 文档仓储接口（T3.7）。
/// </summary>
public interface IPpapDocumentRepository
{
    Task<PpapDocument?> GetByIdAsync(Ulid id, CancellationToken ct = default);
    Task<List<PpapDocument>> GetBySupplierAsync(Ulid supplierId, CancellationToken ct = default);
    Task<List<PpapDocument>> GetByMaterialAsync(string materialCode, CancellationToken ct = default);
    Task<List<PpapDocument>> GetExpiringAsync(int daysWithin = 30, CancellationToken ct = default);
    Task<List<PpapDocument>> GetByStatusAsync(PpapStatus status, CancellationToken ct = default);
    Task AddAsync(PpapDocument document, CancellationToken ct = default);
    void Update(PpapDocument document);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// 关键供应商管控设置仓储接口（T3.8）。
/// </summary>
public interface ICriticalSupplierSettingRepository
{
    Task<CriticalSupplierSetting?> GetByIdAsync(Ulid id, CancellationToken ct = default);
    Task<List<CriticalSupplierSetting>> GetAllAsync(CancellationToken ct = default);
    Task<CriticalSupplierSetting?> GetByMaterialCodeAsync(string materialCode, CancellationToken ct = default);
    Task AddAsync(CriticalSupplierSetting setting, CancellationToken ct = default);
    void Update(CriticalSupplierSetting setting);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
