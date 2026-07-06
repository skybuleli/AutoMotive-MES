using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 工艺路线仓储接口（T3.1 M07）。
/// </summary>
public interface IRoutingRepository
{
    Task<Routing?> GetByIdAsync(Ulid id, CancellationToken ct = default);
    Task<Routing?> GetActiveByProductAsync(string productCode, CancellationToken ct = default);
    Task<List<Routing>> GetByProductAsync(string productCode, CancellationToken ct = default);
    Task<List<Routing>> GetAllAsync(CancellationToken ct = default);
    Task<List<Routing>> GetByEcoStatusAsync(EcoStatus status, CancellationToken ct = default);
    Task AddAsync(Routing routing, CancellationToken ct = default);
    Task UpdateAsync(Routing routing, CancellationToken ct = default);
}
