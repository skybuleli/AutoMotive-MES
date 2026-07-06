using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// Andon 报警事件仓储接口（T2.20）。
/// </summary>
public interface IAndonEventRepository
{
    Task<AndonEvent?> GetByIdAsync(Ulid id, CancellationToken ct = default);

    /// <summary>查询报警列表（支持过滤）</summary>
    Task<List<AndonEvent>> GetListAsync(
        AndonEventStatus? status = null,
        string? equipmentCode = null,
        AndonSeverity? severity = null,
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>获取所有未关闭的报警（供升级服务扫描）</summary>
    Task<List<AndonEvent>> GetActiveAsync(CancellationToken ct = default);

    /// <summary>获取未确认的报警数量</summary>
    Task<int> GetActiveCountAsync(CancellationToken ct = default);

    /// <summary>新增报警</summary>
    Task AddAsync(AndonEvent ev, CancellationToken ct = default);

    /// <summary>更新报警状态</summary>
    Task UpdateAsync(AndonEvent ev, CancellationToken ct = default);

    /// <summary>批量更新（升级服务用）</summary>
    Task UpdateRangeAsync(List<AndonEvent> events, CancellationToken ct = default);
}
