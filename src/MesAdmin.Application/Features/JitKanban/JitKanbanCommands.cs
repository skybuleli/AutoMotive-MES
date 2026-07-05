using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Application.Features.JitKanban;

// ═══════════════════════════════════════════════
// 查询：待处理 JIT 拉动信号列表
// ═══════════════════════════════════════════════

[MemoryPackable]
public sealed partial record ListPendingJitSignalsQuery : ICommand<List<JitSignalDto>>;

internal sealed class ListPendingJitSignalsHandler(
    IJitPullSignalRepository repo) : ICommandHandler<ListPendingJitSignalsQuery, List<JitSignalDto>>
{
    public async Task<List<JitSignalDto>> ExecuteAsync(ListPendingJitSignalsQuery query, CancellationToken ct)
    {
        var signals = await repo.GetPendingAsync(ct);
        return signals.Select(MapToDto).ToList();
    }

    private static JitSignalDto MapToDto(JitPullSignal s) => new(
        s.Id.ToString(),
        s.MaterialCode,
        s.MaterialName,
        s.ShortageQuantity,
        s.Unit,
        s.TargetStation ?? "",
        s.OrderNumber,
        s.Status.ToString(),
        s.CreatedAt,
        s.DeliveredAt,
        s.DeliveredBy,
        s.Remarks);
}

// ═══════════════════════════════════════════════
// 查询：包含已送达/已取消的完整列表
// ═══════════════════════════════════════════════

[MemoryPackable]
public sealed partial record ListAllJitSignalsQuery(int Page, int Size) : ICommand<(List<JitSignalDto> Items, int Total)>;

internal sealed class ListAllJitSignalsHandler(
    IJitPullSignalRepository repo) : ICommandHandler<ListAllJitSignalsQuery, (List<JitSignalDto> Items, int Total)>
{
    public async Task<(List<JitSignalDto> Items, int Total)> ExecuteAsync(ListAllJitSignalsQuery query, CancellationToken ct)
    {
        var skip = (Math.Max(1, query.Page) - 1) * query.Size;
        var take = Math.Clamp(query.Size, 1, 100);
        var items = await repo.GetPageAsync(skip, take, ct);
        var total = await repo.CountAsync(ct);
        return (items.Select(s => new JitSignalDto(
            s.Id.ToString(), s.MaterialCode, s.MaterialName,
            s.ShortageQuantity, s.Unit, s.TargetStation ?? "",
            s.OrderNumber, s.Status.ToString(),
            s.CreatedAt, s.DeliveredAt, s.DeliveredBy, s.Remarks
        )).ToList(), total);
    }
}

// ═══════════════════════════════════════════════
// 命令：确认送达——仓库 PDA 扫码确认
// ═══════════════════════════════════════════════

[MemoryPackable]
public sealed partial record ConfirmDeliveryCommand(
    Ulid SignalId,
    string DeliveredBy) : IWriteCommand<JitSignalDto>;

internal sealed class ConfirmDeliveryHandler(
    IJitPullSignalRepository repo,
    ILogger<ConfirmDeliveryHandler> logger) : ICommandHandler<ConfirmDeliveryCommand, JitSignalDto>
{
    public async Task<JitSignalDto> ExecuteAsync(ConfirmDeliveryCommand cmd, CancellationToken ct)
    {
        var signal = await repo.GetByIdTrackedAsync(cmd.SignalId, ct)
            ?? throw new KeyNotFoundException($"JIT 拉动信号 {cmd.SignalId} 不存在");

        signal.Deliver(cmd.DeliveredBy, DateTimeOffset.UtcNow);
        await repo.SaveChangesAsync(ct);

        logger.ZLogInformation($"JIT 拉动送达确认：物料 {signal.MaterialCode} 数量 {signal.ShortageQuantity}{signal.Unit} → {signal.TargetStation}，操作员 {cmd.DeliveredBy}");

        return new JitSignalDto(
            signal.Id.ToString(), signal.MaterialCode, signal.MaterialName,
            signal.ShortageQuantity, signal.Unit, signal.TargetStation ?? "",
            signal.OrderNumber, signal.Status.ToString(),
            signal.CreatedAt, signal.DeliveredAt, signal.DeliveredBy, signal.Remarks);
    }
}

// ═══════════════════════════════════════════════
// 命令：取消 JIT 拉动信号
// ═══════════════════════════════════════════════

[MemoryPackable]
public sealed partial record CancelJitSignalCommand(
    Ulid SignalId,
    string Reason) : IWriteCommand<JitSignalDto>;

internal sealed class CancelJitSignalHandler(
    IJitPullSignalRepository repo,
    ILogger<CancelJitSignalHandler> logger) : ICommandHandler<CancelJitSignalCommand, JitSignalDto>
{
    public async Task<JitSignalDto> ExecuteAsync(CancelJitSignalCommand cmd, CancellationToken ct)
    {
        var signal = await repo.GetByIdTrackedAsync(cmd.SignalId, ct)
            ?? throw new KeyNotFoundException($"JIT 拉动信号 {cmd.SignalId} 不存在");

        signal.Cancel(cmd.Reason);
        await repo.SaveChangesAsync(ct);

        logger.ZLogInformation($"JIT 拉动已取消：{signal.MaterialCode}，原因：{cmd.Reason}");

        return new JitSignalDto(
            signal.Id.ToString(), signal.MaterialCode, signal.MaterialName,
            signal.ShortageQuantity, signal.Unit, signal.TargetStation ?? "",
            signal.OrderNumber, signal.Status.ToString(),
            signal.CreatedAt, signal.DeliveredAt, signal.DeliveredBy, signal.Remarks);
    }
}

// ═══════════════════════════════════════════════
// 命令：手动创建 JIT 拉动信号（空料箱扫码入口）
// ═══════════════════════════════════════════════

[MemoryPackable]
public sealed partial record CreateJitSignalCommand(
    Ulid? OrderId,
    string MaterialCode,
    string MaterialName,
    double ShortageQuantity,
    string Unit,
    string? TargetStation,
    string? CreatedBy) : IWriteCommand<JitSignalDto>;

internal sealed class CreateJitSignalHandler(
    IJitPullSignalRepository repo,
    ILogger<CreateJitSignalHandler> logger) : ICommandHandler<CreateJitSignalCommand, JitSignalDto>
{
    public async Task<JitSignalDto> ExecuteAsync(CreateJitSignalCommand cmd, CancellationToken ct)
    {
        var orderId = cmd.OrderId ?? Ulid.Empty;
        var orderNumber = cmd.OrderId is not null ? $"WO-关联工单" : $"SCAN-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";

        var signal = JitPullSignal.Create(
            orderId,
            orderNumber,
            cmd.MaterialCode,
            cmd.MaterialName,
            cmd.ShortageQuantity,
            cmd.Unit,
            cmd.TargetStation);

        await repo.AddAsync(signal, ct);
        await repo.SaveChangesAsync(ct);

        logger.ZLogInformation($"JIT 拉动手动创建：{cmd.MaterialCode} × {cmd.ShortageQuantity}{cmd.Unit} → {cmd.TargetStation ?? "线边"}");

        return new JitSignalDto(
            signal.Id.ToString(), signal.MaterialCode, signal.MaterialName,
            signal.ShortageQuantity, signal.Unit, signal.TargetStation ?? "",
            signal.OrderNumber, signal.Status.ToString(),
            signal.CreatedAt, signal.DeliveredAt, signal.DeliveredBy, signal.Remarks);
    }
}

// ═══════════════════════════════════════════════
// DTO
// ═══════════════════════════════════════════════

[MemoryPackable]
public sealed partial record JitSignalDto(
    string Id,
    string MaterialCode,
    string MaterialName,
    double ShortageQuantity,
    string Unit,
    string TargetStation,
    string OrderNumber,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeliveredAt,
    string? DeliveredBy,
    string? Remarks);
