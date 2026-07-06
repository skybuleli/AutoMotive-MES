using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.Quality;

// ═══════════════════════════════════════════════════════════
// T2.2 IQC 来料检验
// ═══════════════════════════════════════════════════════════

[MemoryPackable]
public sealed partial record CreateIqcRecordCommand(
    Ulid InspectionPlanId,
    string InspectionPlanName,
    string MaterialCode,
    string MaterialName,
    string BatchNumber,
    string SupplierCode,
    string SupplierName,
    string InspectorId,
    int SampleSize,
    int AcceptNumber,
    int RejectNumber,
    string? AqlScheme) : IWriteCommand<QualityRecord>;

internal sealed class CreateIqcRecordHandler(
    IQualityRecordRepository repo) : ICommandHandler<CreateIqcRecordCommand, QualityRecord>
{
    public async Task<QualityRecord> ExecuteAsync(CreateIqcRecordCommand cmd, CancellationToken ct)
    {
        var record = QualityRecord.CreateIqc(
            cmd.InspectionPlanId, cmd.InspectionPlanName,
            cmd.MaterialCode, cmd.MaterialName, cmd.BatchNumber,
            cmd.SupplierCode, cmd.SupplierName, cmd.InspectorId,
            cmd.SampleSize, cmd.AcceptNumber, cmd.RejectNumber,
            cmd.AqlScheme);

        await repo.AddAsync(record, ct);
        await repo.SaveChangesAsync(ct);
        return record;
    }
}

[MemoryPackable]
public sealed partial record RecordIqcMeasurementCommand(
    Ulid RecordId,
    string CharacteristicCode,
    double ActualValue) : IWriteCommand<QualityRecord>;

internal sealed class RecordIqcMeasurementHandler(
    IQualityRecordRepository repo) : ICommandHandler<RecordIqcMeasurementCommand, QualityRecord>
{
    public async Task<QualityRecord> ExecuteAsync(RecordIqcMeasurementCommand cmd, CancellationToken ct)
    {
        var record = await repo.GetByIdTrackedAsync(cmd.RecordId, ct)
            ?? throw new KeyNotFoundException($"检验记录 {cmd.RecordId} 不存在");
        record.RecordCharacteristic(cmd.CharacteristicCode, cmd.ActualValue);
        await repo.SaveChangesAsync(ct);
        return record;
    }
}

[MemoryPackable]
public sealed partial record CompleteQualityRecordCommand(
    Ulid RecordId) : IWriteCommand<QualityRecord>;

internal sealed class CompleteQualityRecordHandler(
    IQualityRecordRepository repo,
    INonConformanceReportRepository ncrRepo) : ICommandHandler<CompleteQualityRecordCommand, QualityRecord>
{
    public async Task<QualityRecord> ExecuteAsync(CompleteQualityRecordCommand cmd, CancellationToken ct)
    {
        var record = await repo.GetByIdTrackedAsync(cmd.RecordId, ct)
            ?? throw new KeyNotFoundException($"检验记录 {cmd.RecordId} 不存在");

        record.Complete();

        // 自动创建 NCR（如果判定为不合格）
        if (record.Verdict == InspectionVerdict.Failed)
        {
            var description = $"检验 {record.InspectionPlanName} 不合格: "
                + $"{string.Join("; ", record.Characteristics.Where(c => c.IsFailed).Select(c => $"{c.CharacteristicName}({c.ActualValue})"))}";

            var severity = record.Stage == InspectionStage.Iq ? NcrSeverity.Major : NcrSeverity.Minor;
            var ncr = NonConformanceReport.CreateFromQualityRecord(record, description, record.DefectCount, severity);
            await ncrRepo.AddAsync(ncr, ct);
        }

        await repo.SaveChangesAsync(ct);
        return record;
    }
}

// ═══════════════════════════════════════════════════════════
// T2.4 IPQC 过程巡检
// ═══════════════════════════════════════════════════════════

[MemoryPackable]
public sealed partial record CreateIpqcRecordCommand(
    Ulid OrderId,
    string OrderNumber,
    string ProductCode,
    string ProductName,
    Ulid InspectionPlanId,
    string InspectionPlanName,
    string InspectorId,
    List<MeasuredCharacteristic> Characteristics,
    int AcceptNumber = 0,
    int RejectNumber = 1) : IWriteCommand<QualityRecord>;

internal sealed class CreateIpqcRecordHandler(
    IQualityRecordRepository repo) : ICommandHandler<CreateIpqcRecordCommand, QualityRecord>
{
    public async Task<QualityRecord> ExecuteAsync(CreateIpqcRecordCommand cmd, CancellationToken ct)
    {
        var record = QualityRecord.CreateIpqc(
            cmd.OrderId, cmd.OrderNumber, cmd.ProductCode, cmd.ProductName,
            cmd.InspectionPlanId, cmd.InspectionPlanName, cmd.InspectorId,
            cmd.Characteristics, cmd.AcceptNumber, cmd.RejectNumber);

        await repo.AddAsync(record, ct);
        await repo.SaveChangesAsync(ct);
        return record;
    }
}

// ═══════════════════════════════════════════════════════════
// T2.5 SPC 样本 + 控制图检测
// ═══════════════════════════════════════════════════════════

[MemoryPackable]
public sealed partial record RecordSpcSampleCommand(
    string CharacteristicCode,
    Ulid? OrderId,
    string? OrderNumber,
    string? EquipmentCode,
    List<double> Values,
    string Source) : IWriteCommand<SpcSampleResult>;

/// <summary>SPC 样本记录结果：包含样本本身 + 触发的判异规则告警</summary>
[MemoryPackable]
public sealed partial record SpcSampleResult(
    SpcSample Sample,
    List<SpcRuleAlert> Alerts);

internal sealed class RecordSpcSampleHandler(
    ISpcSampleRepository sampleRepo,
    ISpcRuleAlertRepository alertRepo,
    IInspectionPlanRepository planRepo) : ICommandHandler<RecordSpcSampleCommand, SpcSampleResult>
{
    public async Task<SpcSampleResult> ExecuteAsync(RecordSpcSampleCommand cmd, CancellationToken ct)
    {
        var nextIndex = await sampleRepo.GetMaxSubgroupIndexAsync(cmd.CharacteristicCode, ct) + 1;
        var values = cmd.Values.ToArray();

        var sample = SpcSample.Create(
            cmd.CharacteristicCode, nextIndex, values.AsSpan(),
            cmd.OrderId, cmd.OrderNumber, cmd.EquipmentCode);
        await sampleRepo.AddAsync(sample, ct);

        // 检查 Western Electric 判异规则
        var recentSamples = await sampleRepo.GetByCharacteristicAsync(cmd.CharacteristicCode, 25, ct);

        // 从 InspectionPlan 获取控制限
        var alerts = new List<SpcRuleAlert>();
        var plans = await planRepo.GetEnabledAsync(ct);
        var planChars = plans
            .SelectMany(p => p.Characteristics)
            .Where(c => c.CharacteristicCode == cmd.CharacteristicCode && c.EnableSpc)
            .ToList();

        if (planChars.Count != 0 && recentSamples.Count >= 2)
        {
            var pc = planChars[0];
            if (pc.CenterLine.HasValue && pc.UpperControlLimit.HasValue && pc.LowerControlLimit.HasValue)
            {
                alerts = SpcCalculator.CheckWesternElectricRules(
                    recentSamples.ToArray().AsSpan(),
                    pc.CenterLine.Value,
                    pc.UpperControlLimit.Value,
                    pc.LowerControlLimit.Value,
                    cmd.CharacteristicCode,
                    cmd.OrderId,
                    cmd.EquipmentCode);
            }
            else
            {
                // 自动计算控制限（首次添加样本时）
                var (grandMean, meanRange, uclX, lclX, uclR, lclR) =
                    SpcCalculator.CalculateXbarRControlLimits(recentSamples.ToArray().AsSpan());
                alerts = SpcCalculator.CheckWesternElectricRules(
                    recentSamples.ToArray().AsSpan(),
                    grandMean, uclX, lclX,
                    cmd.CharacteristicCode,
                    cmd.OrderId,
                    cmd.EquipmentCode);
            }
        }

        if (alerts.Count > 0)
            await alertRepo.AddRangeAsync(alerts, ct);

        await sampleRepo.SaveChangesAsync(ct);
        return new SpcSampleResult(sample, alerts);
    }
}

[MemoryPackable]
public sealed partial record AcknowledgeSpcAlertCommand(
    Ulid AlertId,
    string AcknowledgedBy,
    string? ActionTaken) : IWriteCommand<SpcRuleAlert>;

internal sealed class AcknowledgeSpcAlertHandler(
    ISpcRuleAlertRepository alertRepo) : ICommandHandler<AcknowledgeSpcAlertCommand, SpcRuleAlert>
{
    public async Task<SpcRuleAlert> ExecuteAsync(AcknowledgeSpcAlertCommand cmd, CancellationToken ct)
    {
        var alerts = await alertRepo.GetUnacknowledgedAsync(ct: ct);
        var alert = alerts.FirstOrDefault(a => a.Id == cmd.AlertId)
            ?? throw new KeyNotFoundException($"SPC 告警 {cmd.AlertId} 不存在或已被确认");

        alert.Acknowledge(cmd.AcknowledgedBy, cmd.ActionTaken);
        alertRepo.Update(alert);
        await alertRepo.SaveChangesAsync(ct);
        return alert;
    }
}

// ═══════════════════════════════════════════════════════════
// T2.7 NCR 不合格品报告
// ═══════════════════════════════════════════════════════════

[MemoryPackable]
public sealed partial record CreateNcrCommand(
    Ulid QualityRecordId,
    string Description,
    int DefectQuantity,
    NcrSeverity Severity) : IWriteCommand<NonConformanceReport>;

internal sealed class CreateNcrHandler(
    IQualityRecordRepository recordRepo,
    INonConformanceReportRepository ncrRepo) : ICommandHandler<CreateNcrCommand, NonConformanceReport>
{
    public async Task<NonConformanceReport> ExecuteAsync(CreateNcrCommand cmd, CancellationToken ct)
    {
        var record = await recordRepo.GetByIdAsync(cmd.QualityRecordId, ct)
            ?? throw new KeyNotFoundException($"检验记录 {cmd.QualityRecordId} 不存在");

        var ncr = NonConformanceReport.CreateFromQualityRecord(
            record, cmd.Description, cmd.DefectQuantity, cmd.Severity);
        await ncrRepo.AddAsync(ncr, ct);
        await ncrRepo.SaveChangesAsync(ct);
        return ncr;
    }
}

[MemoryPackable]
public sealed partial record SubmitNcrForReviewCommand(
    Ulid NcrId,
    string ReviewerId) : IWriteCommand<NonConformanceReport>;

internal sealed class SubmitNcrForReviewHandler(
    INonConformanceReportRepository ncrRepo) : ICommandHandler<SubmitNcrForReviewCommand, NonConformanceReport>
{
    public async Task<NonConformanceReport> ExecuteAsync(SubmitNcrForReviewCommand cmd, CancellationToken ct)
    {
        var ncr = await ncrRepo.GetByIdTrackedAsync(cmd.NcrId, ct)
            ?? throw new KeyNotFoundException($"NCR {cmd.NcrId} 不存在");
        ncr.SubmitForReview(cmd.ReviewerId);
        await ncrRepo.SaveChangesAsync(ct);
        return ncr;
    }
}

[MemoryPackable]
public sealed partial record DispositionNcrCommand(
    Ulid NcrId,
    NcrDisposition Disposition,
    string Comments) : IWriteCommand<NonConformanceReport>;

internal sealed class DispositionNcrHandler(
    INonConformanceReportRepository ncrRepo) : ICommandHandler<DispositionNcrCommand, NonConformanceReport>
{
    public async Task<NonConformanceReport> ExecuteAsync(DispositionNcrCommand cmd, CancellationToken ct)
    {
        var ncr = await ncrRepo.GetByIdTrackedAsync(cmd.NcrId, ct)
            ?? throw new KeyNotFoundException($"NCR {cmd.NcrId} 不存在");
        ncr.SetDisposition(cmd.Disposition, cmd.Comments);
        await ncrRepo.SaveChangesAsync(ct);
        return ncr;
    }
}

[MemoryPackable]
public sealed partial record CloseNcrCommand(
    Ulid NcrId,
    string Remarks) : IWriteCommand<NonConformanceReport>;

internal sealed class CloseNcrHandler(
    INonConformanceReportRepository ncrRepo) : ICommandHandler<CloseNcrCommand, NonConformanceReport>
{
    public async Task<NonConformanceReport> ExecuteAsync(CloseNcrCommand cmd, CancellationToken ct)
    {
        var ncr = await ncrRepo.GetByIdTrackedAsync(cmd.NcrId, ct)
            ?? throw new KeyNotFoundException($"NCR {cmd.NcrId} 不存在");
        ncr.Close(cmd.Remarks);
        await ncrRepo.SaveChangesAsync(ct);
        return ncr;
    }
}

// ═══════════════════════════════════════════════════════════
// T2.8 8D 报告
// ═══════════════════════════════════════════════════════════

[MemoryPackable]
public sealed partial record CreateEightDReportCommand(
    Ulid? NonConformanceReportId,
    string? NcrNumber,
    string Title,
    string ProductCode,
    string ProductName) : IWriteCommand<EightDReport>;

internal sealed class CreateEightDReportHandler(
    IEightDReportRepository repo) : ICommandHandler<CreateEightDReportCommand, EightDReport>
{
    public async Task<EightDReport> ExecuteAsync(CreateEightDReportCommand cmd, CancellationToken ct)
    {
        var report = EightDReport.Create(cmd.Title, cmd.ProductCode, cmd.ProductName);
        if (cmd.NonConformanceReportId.HasValue)
            report.NonConformanceReportId = cmd.NonConformanceReportId;
        if (cmd.NcrNumber is not null)
            report.NcrNumber = cmd.NcrNumber;
        await repo.AddAsync(report, ct);
        await repo.SaveChangesAsync(ct);
        return report;
    }
}

[MemoryPackable]
public sealed partial record UpdateEightDReportCommand(
    Ulid ReportId,
    string? TeamLeader,
    string? TeamMembers,
    string? ProblemDescription,
    string? ContainmentAction,
    string? RootCauseAnalysis,
    string? RootCause,
    string? CorrectiveAction,
    string? CorrectiveActionOwner,
    DateTimeOffset? CorrectiveActionDueDate,
    string? VerificationMethod,
    string? VerificationResult,
    string? PreventiveAction,
    int CompletedStep) : IWriteCommand<EightDReport>;

internal sealed class UpdateEightDReportHandler(
    IEightDReportRepository repo) : ICommandHandler<UpdateEightDReportCommand, EightDReport>
{
    public async Task<EightDReport> ExecuteAsync(UpdateEightDReportCommand cmd, CancellationToken ct)
    {
        var report = await repo.GetByIdTrackedAsync(cmd.ReportId, ct)
            ?? throw new KeyNotFoundException($"8D 报告 {cmd.ReportId} 不存在");

        if (cmd.TeamLeader is not null)
            report.SetTeam(cmd.TeamLeader, cmd.TeamMembers ?? "");
        if (cmd.ProblemDescription is not null)
            report.DescribeProblem(cmd.ProblemDescription);
        if (cmd.ContainmentAction is not null)
            report.SetContainment(cmd.ContainmentAction);
        if (cmd.RootCauseAnalysis is not null && cmd.RootCause is not null)
            report.SetRootCause(cmd.RootCauseAnalysis, cmd.RootCause);
        if (cmd.CorrectiveAction is not null && cmd.CorrectiveActionOwner is not null && cmd.CorrectiveActionDueDate.HasValue)
            report.SetCorrectiveAction(cmd.CorrectiveAction, cmd.CorrectiveActionOwner, cmd.CorrectiveActionDueDate.Value);
        if (cmd.VerificationMethod is not null && cmd.VerificationResult is not null)
            report.Verify(cmd.VerificationMethod, cmd.VerificationResult);
        if (cmd.PreventiveAction is not null)
            report.SetPreventiveAction(cmd.PreventiveAction);

        await repo.SaveChangesAsync(ct);
        return report;
    }
}

[MemoryPackable]
public sealed partial record CloseEightDReportCommand(
    Ulid ReportId,
    string Summary) : IWriteCommand<EightDReport>;

internal sealed class CloseEightDReportHandler(
    IEightDReportRepository repo) : ICommandHandler<CloseEightDReportCommand, EightDReport>
{
    public async Task<EightDReport> ExecuteAsync(CloseEightDReportCommand cmd, CancellationToken ct)
    {
        var report = await repo.GetByIdTrackedAsync(cmd.ReportId, ct)
            ?? throw new KeyNotFoundException($"8D 报告 {cmd.ReportId} 不存在");
        report.Close(cmd.Summary);
        await repo.SaveChangesAsync(ct);
        return report;
    }
}
