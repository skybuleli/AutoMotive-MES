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
    double ActualValue,
    Ulid? GaugeId = null) : IWriteCommand<FirstArticleInspection>;

internal sealed class RecordInspectionValueHandler(
    IFirstArticleInspectionRepository repo,
    IGaugeRepository? gaugeRepo = null) : ICommandHandler<RecordInspectionValueCommand, FirstArticleInspection>
{
    public async Task<FirstArticleInspection> ExecuteAsync(RecordInspectionValueCommand cmd, CancellationToken ct)
    {
        var inspection = await repo.GetByIdTrackedAsync(cmd.InspectionId, ct)
            ?? throw new KeyNotFoundException($"首件检验 {cmd.InspectionId} 不存在");

        if (gaugeRepo is not null)
        {
            if (cmd.GaugeId is { } gid)
            {
                var gauge = await gaugeRepo.GetByIdAsync(gid, ct)
                    ?? throw new KeyNotFoundException($"量具 {gid} 不存在");
                if (!gauge.IsWithinCalibration(DateTimeOffset.UtcNow))
                    throw new InvalidOperationException(
                        $"量具 {gauge.GaugeNumber} 校准已过期或已报废（状态 {gauge.Status}），禁止用于检验");
                // 首次记录时绑定量具到检验单头，供追溯
                inspection.GaugeId ??= gid;
            }
            else
            {
                // 校验：若检验单头未绑定量具，新记录必须提供量具（生产环境强制；测试环境 gaugeRepo 为空时跳过）
                if (inspection.GaugeId is null)
                    throw new InvalidOperationException("首件检验录入实测值必须选择在校准有效期内的量具");
                var existing = await gaugeRepo.GetByIdAsync(inspection.GaugeId.Value, ct);
                if (existing is not null && !existing.IsWithinCalibration(DateTimeOffset.UtcNow))
                    throw new InvalidOperationException(
                        $"已绑定的量具 {existing.GaugeNumber} 校准已过期，禁止继续记录实测值");
            }
        }

        var item = inspection.Items.FirstOrDefault(i => i.CharacteristicCode == cmd.CharacteristicCode)
            ?? throw new KeyNotFoundException($"检验特性 {cmd.CharacteristicCode} 不存在");

        item.RecordValue(cmd.ActualValue);
        await repo.SaveChangesAsync(ct);
        return inspection;
    }
}
