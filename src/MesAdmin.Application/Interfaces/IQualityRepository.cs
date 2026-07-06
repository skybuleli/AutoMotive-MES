using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>检验记录仓储</summary>
public interface IQualityRecordRepository
{
    Task<QualityRecord?> GetByIdAsync(Ulid id, CancellationToken ct = default);
    Task<QualityRecord?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default);
    Task<List<QualityRecord>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default);
    Task<List<QualityRecord>> GetByStageAsync(InspectionStage stage, CancellationToken ct = default);
    Task<List<QualityRecord>> GetByBatchAsync(string batchNumber, CancellationToken ct = default);
    Task AddAsync(QualityRecord record, CancellationToken ct = default);
    void Update(QualityRecord record);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>检验计划仓储</summary>
public interface IInspectionPlanRepository
{
    Task<InspectionPlan?> GetByIdAsync(Ulid id, CancellationToken ct = default);
    Task<List<InspectionPlan>> GetByProductCodeAsync(string productCode, InspectionStage stage, CancellationToken ct = default);
    Task<List<InspectionPlan>> GetEnabledAsync(CancellationToken ct = default);
    Task AddAsync(InspectionPlan plan, CancellationToken ct = default);
    void Update(InspectionPlan plan);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>SPC 样本仓储</summary>
public interface ISpcSampleRepository
{
    Task<SpcSample?> GetByIdAsync(Ulid id, CancellationToken ct = default);
    Task<List<SpcSample>> GetByCharacteristicAsync(string characteristicCode, int limit = 25, CancellationToken ct = default);
    Task<List<SpcSample>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default);
    Task<List<SpcSample>> GetByEquipmentAsync(string equipmentCode, int limit = 25, CancellationToken ct = default);
    Task<int> GetMaxSubgroupIndexAsync(string characteristicCode, CancellationToken ct = default);
    Task AddAsync(SpcSample sample, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<SpcSample> samples, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>SPC 判异告警仓储</summary>
public interface ISpcRuleAlertRepository
{
    Task<List<SpcRuleAlert>> GetUnacknowledgedAsync(string? characteristicCode = null, CancellationToken ct = default);
    Task<List<SpcRuleAlert>> GetByCharacteristicAsync(string characteristicCode, int limit = 50, CancellationToken ct = default);
    Task AddAsync(SpcRuleAlert alert, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<SpcRuleAlert> alerts, CancellationToken ct = default);
    void Update(SpcRuleAlert alert);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>NCR 仓储</summary>
public interface INonConformanceReportRepository
{
    Task<NonConformanceReport?> GetByIdAsync(Ulid id, CancellationToken ct = default);
    Task<NonConformanceReport?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default);
    Task<List<NonConformanceReport>> GetByStatusAsync(NcrStatus status, CancellationToken ct = default);
    Task<List<NonConformanceReport>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default);
    Task<List<NonConformanceReport>> GetByProductCodeAsync(string productCode, CancellationToken ct = default);
    Task AddAsync(NonConformanceReport ncr, CancellationToken ct = default);
    void Update(NonConformanceReport ncr);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>8D 报告仓储</summary>
public interface IEightDReportRepository
{
    Task<EightDReport?> GetByIdAsync(Ulid id, CancellationToken ct = default);
    Task<EightDReport?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default);
    Task<List<EightDReport>> GetByStatusAsync(EightDStatus status, CancellationToken ct = default);
    Task<List<EightDReport>> GetByProductCodeAsync(string productCode, CancellationToken ct = default);
    Task AddAsync(EightDReport report, CancellationToken ct = default);
    void Update(EightDReport report);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
