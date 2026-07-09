using MesAdmin.Application.Common;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 工单仓储接口（Application 层接口，禁止包含实现）。
/// Infrastructure 层提供 EF Core 实现。
/// </summary>
public interface IProductionOrderRepository
{
    /// <summary>查询工单（AsNoTracking，适用于读操作）。</summary>
    Task<ProductionOrder?> GetByIdAsync(Ulid id, CancellationToken cancellationToken = default);
    /// <summary>查询工单（跟踪，适用于写操作——修改属性后直接 SaveChangesAsync 即可，无需 Update）。</summary>
    Task<ProductionOrder?> GetByIdTrackedAsync(Ulid id, CancellationToken cancellationToken = default);
    Task<ProductionOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default);
    Task<List<ProductionOrder>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<List<ProductionOrder>> GetPageAsync(OrderStatus? status, int skip, int take, CancellationToken cancellationToken = default);
    Task<int> CountAsync(OrderStatus? status, CancellationToken cancellationToken = default);
    Task<int> CountByOrderNumberPrefixAsync(string orderNumberPrefix, CancellationToken cancellationToken = default);
    Task AddAsync(ProductionOrder order, CancellationToken cancellationToken = default);
    void Update(ProductionOrder order);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 按多维过滤条件分页查询工单（工单号/产品编码/日期范围/状态）。
    /// 默认实现回退到 status-only 查询；Infrastructure 层覆写为完整 SQL 过滤。
    /// </summary>
    Task<List<ProductionOrder>> GetPageAsync(OrderListFilter filter, int skip, int take, CancellationToken cancellationToken = default)
        => GetPageAsync(filter.Status, skip, take, cancellationToken);

    /// <summary>按多维过滤条件统计工单总数。默认实现回退到 status-only 统计。</summary>
    Task<int> CountAsync(OrderListFilter filter, CancellationToken cancellationToken = default)
        => CountAsync(filter.Status, cancellationToken);
}

/// <summary>
/// 追溯链接仓储接口。
/// </summary>
public interface ITraceabilityLinkRepository
{
    Task<TraceabilityLink?> GetByIdAsync(Ulid id, CancellationToken cancellationToken = default);
    Task<List<TraceabilityLink>> GetByVinOrSerialAsync(string vinOrSerial, CancellationToken cancellationToken = default);
    Task<List<TraceabilityLink>> GetByComponentBatchAsync(string batch, CancellationToken cancellationToken = default);
    Task<List<TraceabilityLink>> GetByMaterialBatchAsync(string batch, CancellationToken cancellationToken = default);
    Task<List<TraceabilityLink>> GetByOrderIdAsync(Ulid orderId, CancellationToken cancellationToken = default);
    /// <summary>获取哈希链最后一条记录（用于写入时链接前驱）</summary>
    Task<TraceabilityLink?> GetLastLinkAsync(CancellationToken cancellationToken = default);
    Task AddAsync(TraceabilityLink link, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
