using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.Inspections;

/// <summary>完成首件检验命令（质量工程师审核）。</summary>
[MemoryPackable]
public sealed partial record CompleteInspectionCommand(
    Ulid InspectionId,
    string InspectorId) : IWriteCommand<FirstArticleInspection>;

internal sealed class CompleteInspectionHandler(
    IFirstArticleInspectionRepository repo) : ICommandHandler<CompleteInspectionCommand, FirstArticleInspection>
{
    public async Task<FirstArticleInspection> ExecuteAsync(CompleteInspectionCommand cmd, CancellationToken ct)
    {
        var inspection = await repo.GetByIdTrackedAsync(cmd.InspectionId, ct)
            ?? throw new KeyNotFoundException($"首件检验 {cmd.InspectionId} 不存在");

        // 状态机修复：Complete 要求 InProgress，Pending 时先启动
        if (inspection.Status == InspectionStatus.Pending)
            inspection.Start();

        var allPassed = inspection.IsAllItemsPassed;
        inspection.Complete(cmd.InspectorId, allPassed);
        await repo.SaveChangesAsync(ct);
        return inspection;
    }
}
