using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 备件仓储接口（T2.18）。
/// </summary>
public interface ISparePartRepository
{
    Task<SparePart?> GetByIdAsync(Ulid id, CancellationToken ct = default);

    /// <summary>获取所有备件</summary>
    Task<List<SparePart>> GetAllAsync(CancellationToken ct = default);

    /// <summary>按设备编码查询备件</summary>
    Task<List<SparePart>> GetByEquipmentAsync(string equipmentCode, CancellationToken ct = default);

    /// <summary>查询库存低于安全阈值的备件</summary>
    Task<List<SparePart>> GetLowStockAsync(CancellationToken ct = default);

    /// <summary>查询需要补货的备件（低于最低库存）</summary>
    Task<List<SparePart>> GetNeedsRestockAsync(CancellationToken ct = default);

    Task AddAsync(SparePart sparePart, CancellationToken ct = default);
    Task UpdateAsync(SparePart sparePart, CancellationToken ct = default);
}

/// <summary>
/// 备件使用记录仓储接口（T2.18）。
/// </summary>
public interface ISparePartUsageRepository
{
    /// <summary>按维护工单查询消耗的备件列表</summary>
    Task<List<SparePartUsage>> GetByWorkOrderAsync(Ulid workOrderId, CancellationToken ct = default);

    /// <summary>按备件查询使用记录</summary>
    Task<List<SparePartUsage>> GetBySparePartAsync(Ulid sparePartId, CancellationToken ct = default);

    Task AddAsync(SparePartUsage usage, CancellationToken ct = default);
}

/// <summary>
/// 采购申请仓储接口（T2.18）。
/// </summary>
public interface IPurchaseRequestRepository
{
    Task<PurchaseRequest?> GetByIdAsync(Ulid id, CancellationToken ct = default);

    /// <summary>查询采购申请列表（支持按状态过滤）</summary>
    Task<List<PurchaseRequest>> GetListAsync(string? status = null, int limit = 50, CancellationToken ct = default);

    /// <summary>查询某备件的未完成采购申请</summary>
    Task<List<PurchaseRequest>> GetPendingBySparePartAsync(Ulid sparePartId, CancellationToken ct = default);

    Task AddAsync(PurchaseRequest request, CancellationToken ct = default);
    Task UpdateAsync(PurchaseRequest request, CancellationToken ct = default);
}
