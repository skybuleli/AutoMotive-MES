using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace MesAdmin.Infrastructure.Data;

/// <summary>质量检验记录仓储</summary>
public class QualityRecordRepository(MesDbContext db) : IQualityRecordRepository
{
    public Task<QualityRecord?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.QualityRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<QualityRecord?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default)
        => db.QualityRecords.Include(r => r.Characteristics).FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<List<QualityRecord>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
        => db.QualityRecords.AsNoTracking()
            .Where(r => r.OrderId == orderId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public Task<List<QualityRecord>> GetByStageAsync(InspectionStage stage, CancellationToken ct = default)
        => db.QualityRecords.AsNoTracking()
            .Where(r => r.Stage == stage)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public Task<List<QualityRecord>> GetByBatchAsync(string batchNumber, CancellationToken ct = default)
        => db.QualityRecords.AsNoTracking()
            .Where(r => r.BatchNumber == batchNumber)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public Task AddAsync(QualityRecord record, CancellationToken ct = default)
        => db.QualityRecords.AddAsync(record, ct).AsTask();

    public void Update(QualityRecord record) => db.QualityRecords.Update(record);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

/// <summary>检验计划仓储</summary>
public class InspectionPlanRepository(MesDbContext db) : IInspectionPlanRepository
{
    public Task<InspectionPlan?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.InspectionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<List<InspectionPlan>> GetByProductCodeAsync(string productCode, InspectionStage stage, CancellationToken ct = default)
        => db.InspectionPlans.AsNoTracking()
            .Where(p => (p.ProductCode == productCode || p.ProductCode == null) && p.Stage == stage && p.IsEnabled)
            .OrderByDescending(p => p.Version)
            .ToListAsync(ct);

    public Task<List<InspectionPlan>> GetEnabledAsync(CancellationToken ct = default)
        => db.InspectionPlans.AsNoTracking()
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.PlanName)
            .ToListAsync(ct);

    public Task AddAsync(InspectionPlan plan, CancellationToken ct = default)
        => db.InspectionPlans.AddAsync(plan, ct).AsTask();

    public void Update(InspectionPlan plan) => db.InspectionPlans.Update(plan);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

/// <summary>SPC 样本仓储</summary>
public class SpcSampleRepository(MesDbContext db) : ISpcSampleRepository
{
    public Task<SpcSample?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.SpcSamples.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<List<SpcSample>> GetByCharacteristicAsync(string characteristicCode, int limit = 25, CancellationToken ct = default)
        => db.SpcSamples.AsNoTracking()
            .Where(s => s.CharacteristicCode == characteristicCode)
            .OrderByDescending(s => s.SubgroupIndex)
            .Take(limit)
            .OrderBy(s => s.SubgroupIndex)
            .ToListAsync(ct);

    public Task<List<SpcSample>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
        => db.SpcSamples.AsNoTracking()
            .Where(s => s.OrderId == orderId)
            .OrderBy(s => s.SubgroupIndex)
            .ToListAsync(ct);

    public Task<List<SpcSample>> GetByEquipmentAsync(string equipmentCode, int limit = 25, CancellationToken ct = default)
        => db.SpcSamples.AsNoTracking()
            .Where(s => s.EquipmentCode == equipmentCode)
            .OrderByDescending(s => s.CollectedAt)
            .Take(limit)
            .OrderBy(s => s.CollectedAt)
            .ToListAsync(ct);

    public async Task<int> GetMaxSubgroupIndexAsync(string characteristicCode, CancellationToken ct = default)
    {
        var max = await db.SpcSamples.AsNoTracking()
            .Where(s => s.CharacteristicCode == characteristicCode)
            .MaxAsync(s => (int?)s.SubgroupIndex, ct);
        return max ?? 0;
    }

    public Task AddAsync(SpcSample sample, CancellationToken ct = default)
        => db.SpcSamples.AddAsync(sample, ct).AsTask();

    public async Task AddRangeAsync(IEnumerable<SpcSample> samples, CancellationToken ct = default)
        => await db.SpcSamples.AddRangeAsync(samples, ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

/// <summary>SPC 判异告警仓储</summary>
public class SpcRuleAlertRepository(MesDbContext db) : ISpcRuleAlertRepository
{
    public Task<List<SpcRuleAlert>> GetUnacknowledgedAsync(string? characteristicCode = null, CancellationToken ct = default)
    {
        var query = db.SpcRuleAlerts.AsNoTracking().Where(a => !a.IsAcknowledged);
        if (characteristicCode is not null)
            query = query.Where(a => a.CharacteristicCode == characteristicCode);
        return query.OrderByDescending(a => a.CreatedAt).ToListAsync(ct);
    }

    public Task<List<SpcRuleAlert>> GetByCharacteristicAsync(string characteristicCode, int limit = 50, CancellationToken ct = default)
        => db.SpcRuleAlerts.AsNoTracking()
            .Where(a => a.CharacteristicCode == characteristicCode)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public Task AddAsync(SpcRuleAlert alert, CancellationToken ct = default)
        => db.SpcRuleAlerts.AddAsync(alert, ct).AsTask();

    public async Task AddRangeAsync(IEnumerable<SpcRuleAlert> alerts, CancellationToken ct = default)
        => await db.SpcRuleAlerts.AddRangeAsync(alerts, ct);

    public void Update(SpcRuleAlert alert) => db.SpcRuleAlerts.Update(alert);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

/// <summary>NCR 仓储</summary>
public class NonConformanceReportRepository(MesDbContext db) : INonConformanceReportRepository
{
    public Task<NonConformanceReport?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.NonConformanceReports.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);

    public Task<NonConformanceReport?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default)
        => db.NonConformanceReports.FirstOrDefaultAsync(n => n.Id == id, ct);

    public Task<List<NonConformanceReport>> GetByStatusAsync(NcrStatus status, CancellationToken ct = default)
        => db.NonConformanceReports.AsNoTracking()
            .Where(n => n.Status == status)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(ct);

    public Task<List<NonConformanceReport>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
        => db.NonConformanceReports.AsNoTracking()
            .Where(n => n.OrderId == orderId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(ct);

    public Task<List<NonConformanceReport>> GetByProductCodeAsync(string productCode, CancellationToken ct = default)
        => db.NonConformanceReports.AsNoTracking()
            .Where(n => n.ProductCode == productCode)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(ct);

    public Task AddAsync(NonConformanceReport ncr, CancellationToken ct = default)
        => db.NonConformanceReports.AddAsync(ncr, ct).AsTask();

    public void Update(NonConformanceReport ncr) => db.NonConformanceReports.Update(ncr);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

/// <summary>8D 报告仓储</summary>
public class EightDReportRepository(MesDbContext db) : IEightDReportRepository
{
    public Task<EightDReport?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.EightDReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<EightDReport?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default)
        => db.EightDReports.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<List<EightDReport>> GetByStatusAsync(EightDStatus status, CancellationToken ct = default)
        => db.EightDReports.AsNoTracking()
            .Where(r => r.Status == status)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public Task<List<EightDReport>> GetByProductCodeAsync(string productCode, CancellationToken ct = default)
        => db.EightDReports.AsNoTracking()
            .Where(r => r.ProductCode == productCode)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public Task AddAsync(EightDReport report, CancellationToken ct = default)
        => db.EightDReports.AddAsync(report, ct).AsTask();

    public void Update(EightDReport report) => db.EightDReports.Update(report);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
