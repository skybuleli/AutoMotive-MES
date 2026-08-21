using Microsoft.EntityFrameworkCore;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Data.Converters;

namespace MesAdmin.Infrastructure.Data;

/// <summary>
/// MES 数据库上下文（EF Core + PostgreSQL 17）。
/// 主键 Ulid 通过全局 ValueConverter 存入 PG uuid 列。
/// </summary>
public class MesDbContext : DbContext
{
    public MesDbContext(DbContextOptions<MesDbContext> options) : base(options) { }

    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<TraceabilityLink> TraceabilityLinks => Set<TraceabilityLink>();
    public DbSet<WorkOrderOperation> WorkOrderOperations => Set<WorkOrderOperation>();
    public DbSet<FirstArticleInspection> FirstArticleInspections => Set<FirstArticleInspection>();
    public DbSet<Bom> Boms => Set<Bom>();
    public DbSet<SapRejectionRecord> SapRejectionRecords => Set<SapRejectionRecord>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<MaterialBatch> MaterialBatches => Set<MaterialBatch>();
    public DbSet<MaterialBinding> MaterialBindings => Set<MaterialBinding>();
    public DbSet<JitPullSignal> JitPullSignals => Set<JitPullSignal>();
    public DbSet<MaterialInventorySetting> MaterialInventorySettings => Set<MaterialInventorySetting>();
    public DbSet<InventoryAlert> InventoryAlerts => Set<InventoryAlert>();
    public DbSet<MaterialConsumption> MaterialConsumptions => Set<MaterialConsumption>();
    public DbSet<ConsumptionVarianceReport> ConsumptionVarianceReports => Set<ConsumptionVarianceReport>();
    public DbSet<SapInventorySyncRecord> SapInventorySyncRecords => Set<SapInventorySyncRecord>();

    // ── SPC Quality Management (T2.1-T2.10) ──
    public DbSet<QualityRecord> QualityRecords => Set<QualityRecord>();
    public DbSet<InspectionPlan> InspectionPlans => Set<InspectionPlan>();
    public DbSet<SpcSample> SpcSamples => Set<SpcSample>();
    public DbSet<SpcRuleAlert> SpcRuleAlerts => Set<SpcRuleAlert>();
    public DbSet<NonConformanceReport> NonConformanceReports => Set<NonConformanceReport>();
    public DbSet<EightDReport> EightDReports => Set<EightDReport>();

    // ── Andon (T2.20-T2.23) ──
    public DbSet<AndonEvent> AndonEvents => Set<AndonEvent>();

    // ── 100% 在线液压测试 (T2.6) ──
    public DbSet<HydraulicTestResult> HydraulicTestResults => Set<HydraulicTestResult>();

    // ── 预防性维护 (T2.17) ──
    public DbSet<MaintenancePlan> MaintenancePlans => Set<MaintenancePlan>();
    public DbSet<MaintenanceWorkOrder> MaintenanceWorkOrders => Set<MaintenanceWorkOrder>();

    // ── 备件管理 (T2.18) ──
    public DbSet<SparePart> SpareParts => Set<SparePart>();
    public DbSet<SparePartUsage> SparePartUsages => Set<SparePartUsage>();
    public DbSet<PurchaseRequest> PurchaseRequests => Set<PurchaseRequest>();

    // ── 工艺路线管理 (T3.1/T3.2 M07) ──
    public DbSet<Routing> Routings => Set<Routing>();

    // ── SAP 工单同步记录 (T3.14) ──
    public DbSet<SapOrderSyncRecord> SapOrderSyncRecords => Set<SapOrderSyncRecord>();

    // ── 终端离线同步记录 (T4.4) ──
    public DbSet<OfflineSyncRecord> OfflineSyncRecords => Set<OfflineSyncRecord>();

    // ═══════════════════════════════════════════════════════════
    //  M08 SQE 供应商质量模块实体配置 (T3.6-T3.8)
    // ═══════════════════════════════════════════════════════════

    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierScoreCard> SupplierScoreCards => Set<SupplierScoreCard>();
    public DbSet<PpapDocument> PpapDocuments => Set<PpapDocument>();
    public DbSet<CriticalSupplierSetting> CriticalSupplierSettings => Set<CriticalSupplierSetting>();

    // ═══════════════════════════════════════════════════════════
    //  M09 排程管理实体配置 (T3.10-T3.13)
    // ═══════════════════════════════════════════════════════════

    public DbSet<ProductionSchedule> ProductionSchedules => Set<ProductionSchedule>();
    public DbSet<CapacityCalendar> CapacityCalendars => Set<CapacityCalendar>();

    // ═══════════════════════════════════════════════════════════
    //  系统管理：用户账号 + 操作审计日志（IATF 追溯）
    // ═══════════════════════════════════════════════════════════

    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // 所有 Ulid 属性自动映射为 PG uuid 列，避免在每个实体中重复配置。
        configurationBuilder.Properties<Ulid>().HaveConversion<UlidToGuidConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 按模块拆分的 IEntityTypeConfiguration<T> 自动注册。
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MesDbContext).Assembly);
    }
}
