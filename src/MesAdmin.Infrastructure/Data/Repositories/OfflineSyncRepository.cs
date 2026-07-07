using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace MesAdmin.Infrastructure.Data.Repositories;

/// <summary>
/// 离线同步记录 EF Core 仓储实现（T4.4）。
/// </summary>
public class OfflineSyncRepository(MesDbContext db) : IOfflineSyncRepository
{
    public Task<OfflineSyncRecord?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.OfflineSyncRecords.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<List<OfflineSyncRecord>> GetByTerminalAsync(
        string terminalId,
        string? status = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        var query = db.OfflineSyncRecords.Where(r => r.TerminalId == terminalId);
        if (status is not null)
            query = query.Where(r => r.Status == status);
        return query.OrderByDescending(r => r.CreatedAt).Take(limit).ToListAsync(ct);
    }

    public Task<List<OfflineSyncRecord>> GetPendingAsync(int limit = 200, CancellationToken ct = default)
        => db.OfflineSyncRecords
            .Where(r => OfflineSyncStatus.Retryable.Contains(r.Status))
            .OrderBy(r => r.RetryCount)
            .ThenBy(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public Task<List<OfflineSyncRecord>> GetConflictsAsync(
        string? terminalId = null,
        CancellationToken ct = default)
    {
        var query = db.OfflineSyncRecords.Where(r => r.Status == OfflineSyncStatus.Conflict);
        if (terminalId is not null)
            query = query.Where(r => r.TerminalId == terminalId);
        return query.OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
    }

    public async Task AddRangeAsync(List<OfflineSyncRecord> records, CancellationToken ct = default)
    {
        await db.OfflineSyncRecords.AddRangeAsync(records, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateRangeAsync(List<OfflineSyncRecord> records, CancellationToken ct = default)
    {
        db.OfflineSyncRecords.UpdateRange(records);
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkSyncedAsync(Ulid id, CancellationToken ct = default)
    {
        var record = await db.OfflineSyncRecords.FindAsync([id], ct);
        if (record is not null)
        {
            record.Status = OfflineSyncStatus.Synced;
            record.SyncedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task MarkFailedAsync(Ulid id, string errorMessage, CancellationToken ct = default)
    {
        var record = await db.OfflineSyncRecords.FindAsync([id], ct);
        if (record is not null)
        {
            record.Status = OfflineSyncStatus.Failed;
            record.ErrorMessage = errorMessage;
            record.RetryCount++;
            record.LastAttemptAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task MarkConflictAsync(Ulid id, string errorMessage, CancellationToken ct = default)
    {
        var record = await db.OfflineSyncRecords.FindAsync([id], ct);
        if (record is not null)
        {
            record.Status = OfflineSyncStatus.Conflict;
            record.ErrorMessage = errorMessage;
            record.LastAttemptAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<int> DeleteSyncedBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        var toDelete = await db.OfflineSyncRecords
            .Where(r => r.Status == OfflineSyncStatus.Synced && r.SyncedAt < cutoff)
            .ToListAsync(ct);
        var count = toDelete.Count;
        if (count > 0)
        {
            db.OfflineSyncRecords.RemoveRange(toDelete);
            await db.SaveChangesAsync(ct);
        }
        return count;
    }

    public async Task<OfflineSyncStats> GetStatsAsync(CancellationToken ct = default)
    {
        var query = db.OfflineSyncRecords;
        var pending = await query.CountAsync(r => r.Status == OfflineSyncStatus.Pending, ct);
        var synced = await query.CountAsync(r => r.Status == OfflineSyncStatus.Synced, ct);
        var conflict = await query.CountAsync(r => r.Status == OfflineSyncStatus.Conflict, ct);
        var failed = await query.CountAsync(r => r.Status == OfflineSyncStatus.Failed, ct);
        var total = await query.CountAsync(ct);
        var terminals = await query.Select(r => r.TerminalId).Distinct().CountAsync(ct);

        return new OfflineSyncStats(pending, synced, conflict, failed, total, terminals);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
