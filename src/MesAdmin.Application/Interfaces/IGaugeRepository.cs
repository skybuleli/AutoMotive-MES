using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 计量器具仓储接口（S01 · IATF 16949 计量管理）。
/// </summary>
public interface IGaugeRepository
{
    Task<Gauge?> GetByIdAsync(Ulid id, CancellationToken ct = default);

    /// <summary>按器具编号查询（唯一索引，用于建账查重）</summary>
    Task<Gauge?> GetByNumberAsync(string gaugeNumber, CancellationToken ct = default);

    /// <summary>台账列表（可按状态过滤）</summary>
    Task<List<Gauge>> GetAllAsync(GaugeStatus? status = null, CancellationToken ct = default);

    Task AddAsync(Gauge gauge, CancellationToken ct = default);
    Task UpdateAsync(Gauge gauge, CancellationToken ct = default);
}
