using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.Traceability;

/// <summary>
/// 追溯绑定命令（T1.20）。
/// 装配工位扫码绑定 ECU/HCU/电机 S/N → 电磁阀批次 → 阀体批次。
/// 写入 traceability_links 表，使用 Effect.AtLeastOnce + DB 唯一约束防重复。
/// 哈希链：每条记录链接前一条记录的 Hash，保证追溯链不可篡改。
/// </summary>
[MemoryPackable]
public sealed partial record BindTraceabilityCommand(
    Ulid OrderId,
    TraceabilityLevel Level,
    string VinOrSerial,
    string? ComponentBatch,
    string? MaterialBatch) : IWriteCommand<TraceabilityLink>;

internal sealed class BindTraceabilityHandler(
    ITraceabilityLinkRepository repo) : ICommandHandler<BindTraceabilityCommand, TraceabilityLink>
{
    public async Task<TraceabilityLink> ExecuteAsync(BindTraceabilityCommand cmd, CancellationToken ct)
    {
        // 幂等：同 VinOrSerial + Level 已存在则直接返回（DB 唯一约束也兜底）
        var existing = await repo.GetByVinOrSerialAsync(cmd.VinOrSerial, ct);
        var dup = existing.FirstOrDefault(l => l.Level == cmd.Level);
        if (dup is not null)
            return dup;

        // 哈希链：取最后一条记录的 Hash 作为前驱
        var lastLink = await repo.GetLastLinkAsync(ct);
        var previousHash = lastLink?.Hash ?? string.Empty;

        var link = TraceabilityLink.Create(
            cmd.OrderId,
            cmd.Level,
            cmd.VinOrSerial,
            cmd.ComponentBatch ?? string.Empty,
            cmd.MaterialBatch ?? string.Empty,
            previousHash,
            DateTimeOffset.UtcNow);

        await repo.AddAsync(link, ct);
        await repo.SaveChangesAsync(ct);
        return link;
    }
}

/// <summary>正向追溯查询命令（T1.22）：VIN/总成 S/N → 工单 → 零部件 → 原材料</summary>
[MemoryPackable]
public sealed partial record ForwardTraceQuery(string VinOrSerial) : ICommand<List<TraceabilityLink>>;

internal sealed class ForwardTraceHandler(ITraceabilityLinkRepository repo)
    : ICommandHandler<ForwardTraceQuery, List<TraceabilityLink>>
{
    public Task<List<TraceabilityLink>> ExecuteAsync(ForwardTraceQuery query, CancellationToken ct)
        => repo.GetByVinOrSerialAsync(query.VinOrSerial, ct);
}

/// <summary>反向追溯查询命令（T1.23）：原材料/零部件批次 → 所有总成 S/N → 所有 VIN</summary>
[MemoryPackable]
public sealed partial record ReverseTraceQuery(string Batch, TraceabilityBatchType BatchType) : ICommand<List<TraceabilityLink>>;

internal sealed class ReverseTraceHandler(ITraceabilityLinkRepository repo)
    : ICommandHandler<ReverseTraceQuery, List<TraceabilityLink>>
{
    public Task<List<TraceabilityLink>> ExecuteAsync(ReverseTraceQuery query, CancellationToken ct)
        => query.BatchType switch
        {
            TraceabilityBatchType.Component => repo.GetByComponentBatchAsync(query.Batch, ct),
            TraceabilityBatchType.Material => repo.GetByMaterialBatchAsync(query.Batch, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(query.BatchType))
        };
}

/// <summary>反向追溯批次类型</summary>
public enum TraceabilityBatchType
{
    /// <summary>零部件批次（电磁阀/ECU/HCU/电机）</summary>
    Component = 0,

    /// <summary>原材料批次（阀体铝合金/PCB 板材）</summary>
    Material = 1
}
