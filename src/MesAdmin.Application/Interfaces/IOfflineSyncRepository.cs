using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 离线同步记录仓储接口（T4.4）。
/// 管理终端离线期间产生的操作记录的持久化与查询。
/// </summary>
public interface IOfflineSyncRepository
{
    /// <summary>按 Id 查询</summary>
    Task<OfflineSyncRecord?> GetByIdAsync(Ulid id, CancellationToken ct = default);

    /// <summary>查询终端的离线记录列表</summary>
    Task<List<OfflineSyncRecord>> GetByTerminalAsync(
        string terminalId,
        string? status = null,
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>查询待同步的记录（支持跨终端聚合）</summary>
    Task<List<OfflineSyncRecord>> GetPendingAsync(
        int limit = 200,
        CancellationToken ct = default);

    /// <summary>查询冲突记录</summary>
    Task<List<OfflineSyncRecord>> GetConflictsAsync(
        string? terminalId = null,
        CancellationToken ct = default);

    /// <summary>批量新增记录（终端上传时调用）</summary>
    Task AddRangeAsync(List<OfflineSyncRecord> records, CancellationToken ct = default);

    /// <summary>批量更新状态</summary>
    Task UpdateRangeAsync(List<OfflineSyncRecord> records, CancellationToken ct = default);

    /// <summary>标记为同步成功</summary>
    Task MarkSyncedAsync(Ulid id, CancellationToken ct = default);

    /// <summary>标记为失败</summary>
    Task MarkFailedAsync(Ulid id, string errorMessage, CancellationToken ct = default);

    /// <summary>标记为冲突</summary>
    Task MarkConflictAsync(Ulid id, string errorMessage, CancellationToken ct = default);

    /// <summary>获取统计信息</summary>
    Task<OfflineSyncStats> GetStatsAsync(CancellationToken ct = default);

    /// <summary>删除指定日期之前已同步的记录（保留 7 天）</summary>
    Task<int> DeleteSyncedBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default);

    /// <summary>保存变更</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>离线同步统计</summary>
public sealed record OfflineSyncStats(
    int PendingCount,
    int SyncedCount,
    int ConflictCount,
    int FailedCount,
    int TotalCount,
    int TerminalCount);
