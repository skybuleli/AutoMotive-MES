using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.Inspections;

/// <summary>记录检验项实测值命令。</summary>
[MemoryPackable]
public sealed partial record RecordInspectionValueCommand(
    Ulid InspectionId,
    string CharacteristicCode,
    double ActualValue) : IWriteCommand<FirstArticleInspection>;

internal sealed class RecordInspectionValueHandler(
    IFirstArticleInspectionRepository repo) : ICommandHandler<RecordInspectionValueCommand, FirstArticleInspection>
{
    public async Task<FirstArticleInspection> ExecuteAsync(RecordInspectionValueCommand cmd, CancellationToken ct)
    {
        var inspection = await repo.GetByIdAsync(cmd.InspectionId, ct)
            ?? throw new KeyNotFoundException($"首件检验 {cmd.InspectionId} 不存在");

        var item = inspection.Items.FirstOrDefault(i => i.CharacteristicCode == cmd.CharacteristicCode)
            ?? throw new KeyNotFoundException($"检验特性 {cmd.CharacteristicCode} 不存在");

        item.RecordValue(cmd.ActualValue);
        repo.Update(inspection);
        await repo.SaveChangesAsync(ct);
        return inspection;
    }
}
