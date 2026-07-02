using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 工单仓储接口（Application 层接口，禁止包含实现）。
/// Infrastructure 层提供 EF Core 实现。
/// </summary>
public interface IProductionOrderRepository
{
    Task<ProductionOrder?> GetByIdAsync(Ulid id, CancellationToken cancellationToken = default);
    Task<ProductionOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default);
    Task<List<ProductionOrder>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<List<ProductionOrder>> GetPageAsync(OrderStatus? status, int skip, int take, CancellationToken cancellationToken = default);
    Task<int> CountAsync(OrderStatus? status, CancellationToken cancellationToken = default);
    Task<int> CountByOrderNumberPrefixAsync(string orderNumberPrefix, CancellationToken cancellationToken = default);
    Task AddAsync(ProductionOrder order, CancellationToken cancellationToken = default);
    void Update(ProductionOrder order);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
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
    Task AddAsync(TraceabilityLink link, CancellationToken cancellationToken = default);
}
