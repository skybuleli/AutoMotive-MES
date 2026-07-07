using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.Data;

/// <summary>
/// MES 数据库上下文（EF Core + PostgreSQL 17）。
/// 主键 Ulid 通过 UlidToGuidConverter 存入 PG uuid 列。
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── production_orders 表 ──
        modelBuilder.Entity<ProductionOrder>(b =>
        {
            b.ToTable("production_orders");
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).HasConversion<UlidToGuidConverter>();
            b.Property(o => o.OrderNumber).HasMaxLength(32).IsRequired();
            b.HasIndex(o => o.OrderNumber).IsUnique();
            b.Property(o => o.ProductCode).HasMaxLength(32).IsRequired();
            b.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
            b.Property(o => o.BomVersion).HasMaxLength(32);
            b.Property(o => o.RoutingId).HasConversion<UlidToGuidConverter>();
            b.Property(o => o.WorkCenterId).HasMaxLength(32);
            b.Property(o => o.Shift).HasMaxLength(16);
            b.Property(o => o.SourceSystem).HasMaxLength(32);
            b.Property(o => o.ExternalOrderNumber).HasMaxLength(64);
            b.Property(o => o.CancelReason).HasMaxLength(256);
            b.Property(o => o.LineId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(o => o.Status).HasDatabaseName("idx_orders_status");
            b.HasIndex(o => o.CreatedAt).HasDatabaseName("idx_orders_created_at");
            b.Property(o => o.CreatedAt).HasColumnType("timestamptz");
            b.Property(o => o.CompletedAt).HasColumnType("timestamptz");
            b.Property(o => o.PlannedStartAt).HasColumnType("timestamptz");
            b.Property(o => o.PlannedEndAt).HasColumnType("timestamptz");
            b.Property(o => o.ActualStartAt).HasColumnType("timestamptz");
            b.Property(o => o.ActualEndAt).HasColumnType("timestamptz");
        });

        // ── work_order_operations 表（31 工序 × 7 站）──
        modelBuilder.Entity<WorkOrderOperation>(b =>
        {
            b.ToTable("work_order_operations");
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).HasConversion<UlidToGuidConverter>();
            b.Property(o => o.OrderId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(o => new { o.OrderId, o.Sequence }).IsUnique();
            b.Property(o => o.OperationCode).HasMaxLength(32).IsRequired();
            b.Property(o => o.OperationName).HasMaxLength(64).IsRequired();
            b.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
            b.Property(o => o.OperatorId).HasMaxLength(32);
            b.Property(o => o.EquipmentId).HasMaxLength(32);
            b.Property(o => o.FailureReason).HasMaxLength(256);
            b.Property(o => o.Remarks).HasMaxLength(512);
            b.Property(o => o.StartAt).HasColumnType("timestamptz");
            b.Property(o => o.EndAt).HasColumnType("timestamptz");
            b.Property(o => o.CreatedAt).HasColumnType("timestamptz");

            // 过程参数作为 JSONB 存储（PostgreSQL 原生支持，无需额外表）
            b.OwnsMany(o => o.Parameters, p =>
            {
                p.ToJson();
            });
        });

        // ── first_article_inspections 表（首件检验）──
        modelBuilder.Entity<FirstArticleInspection>(b =>
        {
            b.ToTable("first_article_inspections");
            b.HasKey(f => f.Id);
            b.Property(f => f.Id).HasConversion<UlidToGuidConverter>();
            b.Property(f => f.OrderId).HasConversion<UlidToGuidConverter>();
            b.Property(f => f.OrderNumber).HasMaxLength(32).IsRequired();
            b.Property(f => f.ProductCode).HasMaxLength(32).IsRequired();
            b.Property(f => f.InspectionType).HasMaxLength(32).IsRequired();
            b.Property(f => f.Status).HasConversion<string>().HasMaxLength(16);
            b.Property(f => f.OperatorId).HasMaxLength(32);
            b.Property(f => f.InspectorId).HasMaxLength(32);
            b.Property(f => f.Conclusion).HasMaxLength(256);
            b.Property(f => f.CreatedAt).HasColumnType("timestamptz");
            b.Property(f => f.CompletedAt).HasColumnType("timestamptz");
            b.OwnsMany(f => f.Items, p =>
            {
                p.ToJson();
            });
        });

        // ── boms 表（物料清单）──
        modelBuilder.Entity<Bom>(bom =>
        {
            bom.ToTable("boms");
            bom.HasKey(x => x.Id);
            bom.Property(x => x.Id).HasConversion<UlidToGuidConverter>();
            bom.Property(x => x.ProductCode).HasMaxLength(32).IsRequired();
            bom.Property(x => x.Version).HasMaxLength(32).IsRequired();
            bom.HasIndex(x => new { x.ProductCode, x.Version }).IsUnique();
            bom.Property(x => x.EffectiveDate).HasColumnType("timestamptz");
            bom.Property(x => x.ExpirationDate).HasColumnType("timestamptz");
            bom.Property(x => x.CreatedAt).HasColumnType("timestamptz");
            bom.OwnsMany(x => x.Items, p =>
            {
                p.ToJson();
            });
        });

        // ── traceability_links 表（4 级追溯）──
        modelBuilder.Entity<TraceabilityLink>(b =>
        {
            b.ToTable("traceability_links");
            b.HasKey(l => l.Id);
            b.Property(l => l.Id).HasConversion<UlidToGuidConverter>();
            b.Property(l => l.OrderId).HasConversion<UlidToGuidConverter>();
            b.Property(l => l.VinOrSerial).HasMaxLength(64);
            b.HasIndex(l => l.VinOrSerial).HasDatabaseName("idx_trace_vin");
            b.Property(l => l.ComponentBatch).HasMaxLength(64);
            b.HasIndex(l => l.ComponentBatch).HasDatabaseName("idx_trace_component");
            b.Property(l => l.MaterialBatch).HasMaxLength(64);
            b.HasIndex(l => l.MaterialBatch).HasDatabaseName("idx_trace_material");
            b.Property(l => l.CreatedAt).HasColumnType("timestamptz");
            // 唯一约束防重复绑定（Effect.AtLeastOnce 幂等保护）
            b.HasIndex(l => new { l.VinOrSerial, l.Level }).IsUnique();
        });

        // ── sap_rejection_records 表（T1.3 SAP 拒单回写）──
        modelBuilder.Entity<SapRejectionRecord>(b =>
        {
            b.ToTable("sap_rejection_records");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasConversion<UlidToGuidConverter>();
            b.Property(r => r.ExternalOrderNumber).HasMaxLength(64);
            b.HasIndex(r => r.ExternalOrderNumber).HasDatabaseName("idx_sap_rejection_external");
            b.Property(r => r.ProductCode).HasMaxLength(32);
            b.Property(r => r.BomVersion).HasMaxLength(32);
            b.Property(r => r.RoutingId).HasMaxLength(64);
            b.Property(r => r.RejectionReason).HasMaxLength(512).IsRequired();
            b.Property(r => r.WritebackStatus).HasConversion<string>().HasMaxLength(16);
            b.HasIndex(r => r.WritebackStatus).HasDatabaseName("idx_sap_rejection_writeback");
            b.Property(r => r.WritebackError).HasMaxLength(512);
            b.Property(r => r.RejectedAt).HasColumnType("timestamptz");
            b.Property(r => r.WritebackAt).HasColumnType("timestamptz");
        });

        // ── goods_receipts 表（T1.8 成品入库 + 追溯标签）──
        modelBuilder.Entity<GoodsReceipt>(b =>
        {
            b.ToTable("goods_receipts");
            b.HasKey(g => g.Id);
            b.Property(g => g.Id).HasConversion<UlidToGuidConverter>();
            b.Property(g => g.OrderId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(g => g.OrderId).IsUnique(); // 一张工单只能入库一次
            b.Property(g => g.OrderNumber).HasMaxLength(32).IsRequired();
            b.Property(g => g.ProductCode).HasMaxLength(32).IsRequired();
            b.Property(g => g.ReviewerId).HasMaxLength(32).IsRequired();
            b.Property(g => g.TraceabilityLabelCode).HasMaxLength(64).IsRequired();
            b.HasIndex(g => g.TraceabilityLabelCode).IsUnique();
            b.Property(g => g.ReceivedAt).HasColumnType("timestamptz");
            b.Property(g => g.SapSyncedAt).HasColumnType("timestamptz");
        });

        // ── material_batches 表（T1.12 来料扫码入库）──
        modelBuilder.Entity<MaterialBatch>(b =>
        {
            b.ToTable("material_batches");
            b.HasKey(b2 => b2.Id);
            b.Property(b2 => b2.Id).HasConversion<UlidToGuidConverter>();
            b.Property(b2 => b2.MaterialCode).HasMaxLength(32).IsRequired();
            b.HasIndex(b2 => b2.MaterialCode).HasDatabaseName("idx_material_code");
            b.Property(b2 => b2.MaterialName).HasMaxLength(64).IsRequired();
            b.Property(b2 => b2.BatchNumber).HasMaxLength(64).IsRequired();
            b.HasIndex(b2 => b2.BatchNumber).IsUnique();
            b.Property(b2 => b2.SupplierCode).HasMaxLength(32).IsRequired();
            b.Property(b2 => b2.SupplierName).HasMaxLength(64).IsRequired();
            b.Property(b2 => b2.Unit).HasMaxLength(16).IsRequired();
            b.Property(b2 => b2.Status).HasConversion<string>().HasMaxLength(16);
            b.HasIndex(b2 => b2.Status).HasDatabaseName("idx_material_status");
            b.Property(b2 => b2.ProductionDate).HasColumnType("timestamptz");
            b.Property(b2 => b2.ReceivedAt).HasColumnType("timestamptz");
        });

        // ── material_bindings 表（T1.15 投料批次绑定）──
        modelBuilder.Entity<MaterialBinding>(b =>
        {
            b.ToTable("material_bindings");
            b.HasKey(b2 => b2.Id);
            b.Property(b2 => b2.Id).HasConversion<UlidToGuidConverter>();
            b.Property(b2 => b2.OrderId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(b2 => b2.OrderId).HasDatabaseName("idx_binding_order");
            b.Property(b2 => b2.MaterialBatchId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(b2 => b2.MaterialBatchId).HasDatabaseName("idx_binding_batch");
            b.Property(b2 => b2.MaterialCode).HasMaxLength(32).IsRequired();
            b.Property(b2 => b2.BatchNumber).HasMaxLength(64).IsRequired();
            b.Property(b2 => b2.ProductSerial).HasMaxLength(64).IsRequired();
            b.HasIndex(b2 => b2.ProductSerial).HasDatabaseName("idx_binding_serial");
            b.Property(b2 => b2.OperatorId).HasMaxLength(32).IsRequired();
            b.Property(b2 => b2.BoundAt).HasColumnType("timestamptz");
        });

        // ── material_inventory_settings 表（T1.13 线边库存阈值配置）──
        modelBuilder.Entity<MaterialInventorySetting>(b =>
        {
            b.ToTable("material_inventory_settings");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasConversion<UlidToGuidConverter>();
            b.Property(s => s.MaterialCode).HasMaxLength(32).IsRequired();
            b.HasIndex(s => s.MaterialCode).HasDatabaseName("idx_inv_setting_material");
            b.Property(s => s.MaterialName).HasMaxLength(64).IsRequired();
            b.Property(s => s.StationId).HasMaxLength(32);
            b.HasIndex(s => s.StationId).HasDatabaseName("idx_inv_setting_station");
            b.Property(s => s.Unit).HasMaxLength(16).IsRequired();
            b.Property(s => s.UpdatedBy).HasMaxLength(32);
            b.Property(s => s.CreatedAt).HasColumnType("timestamptz");
            b.Property(s => s.UpdatedAt).HasColumnType("timestamptz");
        });

        // ── inventory_alerts 表（T1.13 库存预警记录）──
        modelBuilder.Entity<InventoryAlert>(b =>
        {
            b.ToTable("inventory_alerts");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).HasConversion<UlidToGuidConverter>();
            b.Property(a => a.MaterialCode).HasMaxLength(32).IsRequired();
            b.HasIndex(a => a.MaterialCode).HasDatabaseName("idx_inv_alert_material");
            b.Property(a => a.MaterialName).HasMaxLength(64).IsRequired();
            b.Property(a => a.StationId).HasMaxLength(32);
            b.Property(a => a.AlertLevel).HasConversion<string>().HasMaxLength(16);
            b.HasIndex(a => a.AlertLevel).HasDatabaseName("idx_inv_alert_level");
            b.Property(a => a.ResolvedBy).HasMaxLength(32);
            b.Property(a => a.Resolution).HasMaxLength(256);
            b.Property(a => a.JitPullSignalId).HasConversion<UlidToGuidConverter>();
            b.Property(a => a.CreatedAt).HasColumnType("timestamptz");
            b.Property(a => a.ResolvedAt).HasColumnType("timestamptz");
        });

        // ── material_consumptions 表（T1.17 物料消耗反冲）──
        modelBuilder.Entity<MaterialConsumption>(b =>
        {
            b.ToTable("material_consumptions");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).HasConversion<UlidToGuidConverter>();
            b.Property(c => c.OrderId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(c => c.OrderId).HasDatabaseName("idx_consumption_order");
            b.HasIndex(c => new { c.OrderId, c.MaterialCode }).IsUnique().HasDatabaseName("ux_consumption_order_material");
            b.Property(c => c.OrderNumber).HasMaxLength(32).IsRequired();
            b.Property(c => c.MaterialCode).HasMaxLength(32).IsRequired();
            b.HasIndex(c => c.MaterialCode).HasDatabaseName("idx_consumption_material");
            b.Property(c => c.MaterialName).HasMaxLength(64).IsRequired();
            b.Property(c => c.Unit).HasMaxLength(16).IsRequired();
            b.Property(c => c.CreatedAt).HasColumnType("timestamptz");
        });

        // ── consumption_variance_reports 表（T1.17 消耗差异报告）──
        modelBuilder.Entity<ConsumptionVarianceReport>(b =>
        {
            b.ToTable("consumption_variance_reports");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasConversion<UlidToGuidConverter>();
            b.Property(r => r.OrderId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(r => r.OrderId).HasDatabaseName("idx_variance_order");
            b.Property(r => r.OrderNumber).HasMaxLength(32).IsRequired();
            b.Property(r => r.MaterialCode).HasMaxLength(32).IsRequired();
            b.Property(r => r.MaterialName).HasMaxLength(64).IsRequired();
            b.Property(r => r.Direction).HasMaxLength(8).IsRequired();
            b.Property(r => r.Unit).HasMaxLength(16).IsRequired();
            b.Property(r => r.ResolvedBy).HasMaxLength(32);
            b.Property(r => r.Resolution).HasMaxLength(512);
            b.Property(r => r.CreatedAt).HasColumnType("timestamptz");
            b.Property(r => r.ResolvedAt).HasColumnType("timestamptz");
        });

        // ── sap_order_sync_records 表（T3.14 工单双向同步）──
        modelBuilder.Entity<SapOrderSyncRecord>(b =>
        {
            b.ToTable("sap_order_sync_records");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasConversion<UlidToGuidConverter>();
            b.Property(r => r.OrderId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(r => r.OrderId).HasDatabaseName("idx_sap_order_sync_order");
            b.Property(r => r.OrderNumber).HasMaxLength(32).IsRequired();
            b.Property(r => r.ExternalOrderNumber).HasMaxLength(64).IsRequired();
            b.HasIndex(r => r.ExternalOrderNumber).HasDatabaseName("idx_sap_order_sync_external");
            b.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
            b.Property(r => r.SapDocumentNumber).HasMaxLength(64);
            b.Property(r => r.SyncError).HasMaxLength(512);
            b.HasIndex(r => r.SapSynced).HasDatabaseName("idx_sap_order_sync_status");
            b.Property(r => r.SyncedAt).HasColumnType("timestamptz");
            b.Property(r => r.CreatedAt).HasColumnType("timestamptz");
        });

        // ── sap_inventory_sync_records 表（T1.17 → T3.14 SAP 同步）──
        modelBuilder.Entity<SapInventorySyncRecord>(b =>
        {
            b.ToTable("sap_inventory_sync_records");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasConversion<UlidToGuidConverter>();
            b.Property(r => r.OrderId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(r => r.OrderId).HasDatabaseName("idx_sap_inv_sync_order");
            b.Property(r => r.OrderNumber).HasMaxLength(32).IsRequired();
            b.Property(r => r.MaterialCode).HasMaxLength(32).IsRequired();
            b.HasIndex(r => r.MaterialCode).HasDatabaseName("idx_sap_inv_sync_material");
            b.Property(r => r.MovementType).HasMaxLength(8).IsRequired();
            b.Property(r => r.Unit).HasMaxLength(16).IsRequired();
            b.Property(r => r.SapDocumentNumber).HasMaxLength(64);
            b.Property(r => r.SyncError).HasMaxLength(512);
            b.HasIndex(r => r.SapSynced).HasDatabaseName("idx_sap_inv_sync_status");
            b.Property(r => r.SyncedAt).HasColumnType("timestamptz");
            b.Property(r => r.CreatedAt).HasColumnType("timestamptz");
        });

        // ── jit_pull_signals 表（T1.4 齐套缺料 JIT 拉动）──
        modelBuilder.Entity<JitPullSignal>(b =>
        {
            b.ToTable("jit_pull_signals");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasConversion<UlidToGuidConverter>();
            b.Property(s => s.OrderId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(s => s.OrderId).HasDatabaseName("idx_jit_pull_order");
            b.Property(s => s.OrderNumber).HasMaxLength(32).IsRequired();
            b.Property(s => s.MaterialCode).HasMaxLength(32).IsRequired();
            b.HasIndex(s => s.MaterialCode).HasDatabaseName("idx_jit_pull_material");
            b.Property(s => s.MaterialName).HasMaxLength(64).IsRequired();
            b.Property(s => s.Unit).HasMaxLength(16).IsRequired();
            b.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
            b.HasIndex(s => s.Status).HasDatabaseName("idx_jit_pull_status");
            b.Property(s => s.TargetStation).HasMaxLength(32);
            b.Property(s => s.DeliveredBy).HasMaxLength(32);
            b.Property(s => s.Remarks).HasMaxLength(256);
            b.Property(s => s.CreatedAt).HasColumnType("timestamptz");
            b.Property(s => s.DeliveredAt).HasColumnType("timestamptz");
        });
        // ═══════════════════════════════════════════════════════════
        //  T2 SPC 质量管理模块实体配置
        // ═══════════════════════════════════════════════════════════

        // ── quality_records 表（T2.1 质量检验记录）──
        modelBuilder.Entity<QualityRecord>(b =>
        {
            b.ToTable("quality_records");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasConversion<UlidToGuidConverter>();
            b.Property(r => r.OrderId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(r => r.OrderId).HasDatabaseName("idx_quality_order");
            b.Property(r => r.Stage).HasConversion<string>().HasMaxLength(16);
            b.HasIndex(r => r.Stage).HasDatabaseName("idx_quality_stage");
            b.Property(r => r.ProductCode).HasMaxLength(32).IsRequired();
            b.HasIndex(r => r.ProductCode).HasDatabaseName("idx_quality_product");
            b.Property(r => r.BatchNumber).HasMaxLength(64);
            b.HasIndex(r => r.BatchNumber).HasDatabaseName("idx_quality_batch");
            b.Property(r => r.InspectionPlanId).HasConversion<UlidToGuidConverter>();
            b.Property(r => r.InspectionPlanName).HasMaxLength(64).IsRequired();
            b.Property(r => r.AqlScheme).HasMaxLength(32);
            b.Property(r => r.InspectorId).HasMaxLength(32).IsRequired();
            b.Property(r => r.Verdict).HasConversion<string>().HasMaxLength(16);
            b.Property(r => r.SupplierCode).HasMaxLength(32);
            b.Property(r => r.SupplierName).HasMaxLength(64);
            b.Property(r => r.OrderNumber).HasMaxLength(32);
            b.Property(r => r.ProductName).HasMaxLength(64).IsRequired();
            b.Property(r => r.Remarks).HasMaxLength(512);
            b.Property(r => r.CreatedAt).HasColumnType("timestamptz");
            b.Property(r => r.CompletedAt).HasColumnType("timestamptz");
            // 检验特性作为 JSONB 存储
            b.OwnsMany(r => r.Characteristics, p =>
            {
                p.ToJson();
                p.Property(c => c.CharacteristicCode).HasMaxLength(32).IsRequired();
                p.Property(c => c.CharacteristicName).HasMaxLength(64).IsRequired();
                p.Property(c => c.Unit).HasMaxLength(16).IsRequired();
                p.Property(c => c.MeasurementTool).HasMaxLength(32);
            });
        });

        // ── inspection_plans 表（T2.1/T2.4 控制计划）──
        modelBuilder.Entity<InspectionPlan>(b =>
        {
            b.ToTable("inspection_plans");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).HasConversion<UlidToGuidConverter>();
            b.Property(p => p.PlanName).HasMaxLength(64).IsRequired();
            b.Property(p => p.Version).HasMaxLength(16).IsRequired();
            b.HasIndex(p => new { p.PlanName, p.Version }).IsUnique();
            b.Property(p => p.ProductCode).HasMaxLength(32);
            b.HasIndex(p => p.ProductCode).HasDatabaseName("idx_inspection_plan_product");
            b.Property(p => p.Stage).HasConversion<string>().HasMaxLength(16);
            b.Property(p => p.SamplingFrequency).HasMaxLength(32).IsRequired();
            b.Property(p => p.InspectionLevel).HasMaxLength(8);
            b.Property(p => p.EffectiveDate).HasColumnType("timestamptz");
            b.Property(p => p.ExpirationDate).HasColumnType("timestamptz");
            b.Property(p => p.CreatedAt).HasColumnType("timestamptz");
            // 检验特性定义作为 JSONB 存储
            b.OwnsMany(p => p.Characteristics, c =>
            {
                c.ToJson();
                c.Property(x => x.CharacteristicCode).HasMaxLength(32).IsRequired();
                c.Property(x => x.CharacteristicName).HasMaxLength(64).IsRequired();
                c.Property(x => x.Unit).HasMaxLength(16).IsRequired();
                c.Property(x => x.MeasurementTool).HasMaxLength(32);
            });
        });

        // ── spc_samples 表（T2.5 SPC 样本子组）──
        modelBuilder.Entity<SpcSample>(b =>
        {
            b.ToTable("spc_samples");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasConversion<UlidToGuidConverter>();
            b.Property(s => s.CharacteristicCode).HasMaxLength(32).IsRequired();
            b.HasIndex(s => s.CharacteristicCode).HasDatabaseName("idx_spc_characteristic");
            b.Property(s => s.OrderId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(s => s.OrderId).HasDatabaseName("idx_spc_order");
            b.Property(s => s.OrderNumber).HasMaxLength(32);
            b.Property(s => s.EquipmentCode).HasMaxLength(32);
            b.HasIndex(s => s.EquipmentCode).HasDatabaseName("idx_spc_equipment");
            b.HasIndex(s => s.SubgroupIndex).HasDatabaseName("idx_spc_subgroup");
            b.HasIndex(s => new { s.CharacteristicCode, s.SubgroupIndex }).IsUnique();
            b.Property(s => s.Source).HasMaxLength(8);
            // 子组测量值作为 JSONB
            b.Property(s => s.Values).HasColumnType("jsonb");
            b.Property(s => s.CollectedAt).HasColumnType("timestamptz");
            b.HasIndex(s => s.CollectedAt).HasDatabaseName("idx_spc_collected_at");
        });

        // ── spc_rule_alerts 表（T2.5 Western Electric 判异告警）──
        modelBuilder.Entity<SpcRuleAlert>(b =>
        {
            b.ToTable("spc_rule_alerts");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).HasConversion<UlidToGuidConverter>();
            b.Property(a => a.CharacteristicCode).HasMaxLength(32).IsRequired();
            b.HasIndex(a => a.CharacteristicCode).HasDatabaseName("idx_spc_alert_char");
            b.Property(a => a.RuleType).HasConversion<int>();
            b.Property(a => a.AlertLevel).HasConversion<string>().HasMaxLength(16);
            b.Property(a => a.TriggerSubgroupId).HasConversion<UlidToGuidConverter>();
            b.Property(a => a.OrderId).HasConversion<UlidToGuidConverter>();
            b.Property(a => a.EquipmentCode).HasMaxLength(32);
            b.Property(a => a.Description).HasMaxLength(256).IsRequired();
            b.Property(a => a.AcknowledgedBy).HasMaxLength(32);
            b.Property(a => a.ActionTaken).HasMaxLength(512);
            b.Property(a => a.AcknowledgedAt).HasColumnType("timestamptz");
            b.Property(a => a.CreatedAt).HasColumnType("timestamptz");
            b.HasIndex(a => a.CreatedAt).HasDatabaseName("idx_spc_alert_created");
        });

        // ── non_conformance_reports 表（T2.7 NCR 不合格品报告）──
        modelBuilder.Entity<NonConformanceReport>(b =>
        {
            b.ToTable("non_conformance_reports");
            b.HasKey(n => n.Id);
            b.Property(n => n.Id).HasConversion<UlidToGuidConverter>();
            b.Property(n => n.NcrNumber).HasMaxLength(32).IsRequired();
            b.HasIndex(n => n.NcrNumber).IsUnique();
            b.Property(n => n.QualityRecordId).HasConversion<UlidToGuidConverter>();
            b.Property(n => n.OrderId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(n => n.OrderId).HasDatabaseName("idx_ncr_order");
            b.Property(n => n.OrderNumber).HasMaxLength(32);
            b.Property(n => n.ProductCode).HasMaxLength(32).IsRequired();
            b.HasIndex(n => n.ProductCode).HasDatabaseName("idx_ncr_product");
            b.Property(n => n.ProductName).HasMaxLength(64).IsRequired();
            b.Property(n => n.BatchNumber).HasMaxLength(64);
            b.HasIndex(n => n.BatchNumber).HasDatabaseName("idx_ncr_batch");
            b.Property(n => n.DiscoveredAt).HasConversion<string>().HasMaxLength(16);
            b.Property(n => n.Description).HasMaxLength(1024).IsRequired();
            b.Property(n => n.Severity).HasConversion<string>().HasMaxLength(16);
            b.Property(n => n.Status).HasConversion<string>().HasMaxLength(16);
            b.HasIndex(n => n.Status).HasDatabaseName("idx_ncr_status");
            b.Property(n => n.Disposition).HasConversion<string>().HasMaxLength(16);
            b.Property(n => n.ResponsibleDept).HasMaxLength(32);
            b.Property(n => n.DiscoveredBy).HasMaxLength(32).IsRequired();
            b.Property(n => n.ReviewerId).HasMaxLength(32);
            b.Property(n => n.ReviewComments).HasMaxLength(512);
            b.Property(n => n.DispositionDeadline).HasColumnType("timestamptz");
            b.Property(n => n.EightDReportId).HasConversion<UlidToGuidConverter>();
            b.Property(n => n.CloseRemarks).HasMaxLength(512);
            b.Property(n => n.CreatedAt).HasColumnType("timestamptz");
            b.Property(n => n.ClosedAt).HasColumnType("timestamptz");
        });

        // ── eight_d_reports 表（T2.8 8D 报告）──
        modelBuilder.Entity<EightDReport>(b =>
        {
            b.ToTable("eight_d_reports");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasConversion<UlidToGuidConverter>();
            b.Property(r => r.ReportNumber).HasMaxLength(32).IsRequired();
            b.HasIndex(r => r.ReportNumber).IsUnique();
            b.Property(r => r.NonConformanceReportId).HasConversion<UlidToGuidConverter>();
            b.Property(r => r.NcrNumber).HasMaxLength(32);
            b.Property(r => r.Title).HasMaxLength(128).IsRequired();
            b.Property(r => r.ProductCode).HasMaxLength(32).IsRequired();
            b.Property(r => r.ProductName).HasMaxLength(64).IsRequired();
            b.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
            b.Property(r => r.TeamLeader).HasMaxLength(32);
            b.Property(r => r.TeamMembers).HasMaxLength(256);
            b.Property(r => r.ProblemDescription).HasMaxLength(2048);
            b.Property(r => r.ContainmentAction).HasMaxLength(1024);
            b.Property(r => r.ContainmentDate).HasColumnType("timestamptz");
            b.Property(r => r.RootCauseAnalysis).HasMaxLength(2048);
            b.Property(r => r.RootCause).HasMaxLength(512);
            b.Property(r => r.CorrectiveAction).HasMaxLength(2048);
            b.Property(r => r.CorrectiveActionOwner).HasMaxLength(32);
            b.Property(r => r.CorrectiveActionDueDate).HasColumnType("timestamptz");
            b.Property(r => r.VerificationMethod).HasMaxLength(512);
            b.Property(r => r.VerificationResult).HasMaxLength(512);
            b.Property(r => r.VerificationDate).HasColumnType("timestamptz");
            b.Property(r => r.PreventiveAction).HasMaxLength(2048);
            b.Property(r => r.Summary).HasMaxLength(1024);
            b.Property(r => r.ClosedAt).HasColumnType("timestamptz");
            b.Property(r => r.CreatedAt).HasColumnType("timestamptz");
            b.Property(r => r.UpdatedAt).HasColumnType("timestamptz");
        });

        // ═══════════════════════════════════════════════════════════
        //  T2 Andon 报警模块实体配置 (T2.20-T2.23)
        // ═══════════════════════════════════════════════════════════

        // ── andon_events 表（T2.20 三级报警）──
        modelBuilder.Entity<AndonEvent>(b =>
        {
            b.ToTable("andon_events");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasConversion<UlidToGuidConverter>();
            b.Property(e => e.EventNumber).HasMaxLength(32).IsRequired();
            b.HasIndex(e => e.EventNumber).IsUnique();
            b.Property(e => e.EquipmentCode).HasMaxLength(32).IsRequired();
            b.HasIndex(e => e.EquipmentCode).HasDatabaseName("idx_andon_equipment");
            b.Property(e => e.AlarmType).HasConversion<string>().HasMaxLength(32);
            b.Property(e => e.Severity).HasConversion<string>().HasMaxLength(16);
            b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
            b.HasIndex(e => e.Status).HasDatabaseName("idx_andon_status");
            b.Property(e => e.Description).HasMaxLength(256).IsRequired();
            b.Property(e => e.ProcessTag).HasMaxLength(32);
            b.Property(e => e.OrderId).HasConversion<UlidToGuidConverter>();
            b.Property(e => e.NonConformanceReportId).HasConversion<UlidToGuidConverter>();
            b.Property(e => e.AcknowledgedBy).HasMaxLength(32);
            b.Property(e => e.Resolution).HasMaxLength(512);
            b.Property(e => e.ResolvedBy).HasMaxLength(32);
            b.Property(e => e.CloseRemarks).HasMaxLength(512);
            b.Property(e => e.OccurredAt).HasColumnType("timestamptz");
            b.HasIndex(e => e.OccurredAt).HasDatabaseName("idx_andon_occurred");
            b.Property(e => e.EscalatedAt).HasColumnType("timestamptz");
            b.Property(e => e.AcknowledgedAt).HasColumnType("timestamptz");
            b.Property(e => e.ResolvedAt).HasColumnType("timestamptz");
            b.Property(e => e.ClosedAt).HasColumnType("timestamptz");
            b.Property(e => e.CreatedAt).HasColumnType("timestamptz");
        });

        // ═══════════════════════════════════════════════════════════
        //  T2.17 预防性维护
        // ═══════════════════════════════════════════════════════════

        // ── maintenance_plans 表 ──
        modelBuilder.Entity<MaintenancePlan>(b =>
        {
            b.ToTable("maintenance_plans");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).HasConversion<UlidToGuidConverter>();
            b.Property(p => p.EquipmentCode).HasMaxLength(32).IsRequired();
            b.HasIndex(p => p.EquipmentCode).HasDatabaseName("idx_mt_plan_equipment");
            b.Property(p => p.EquipmentName).HasMaxLength(64).IsRequired();
            b.Property(p => p.MaintenanceType).HasConversion<int>();
            b.Property(p => p.TaskDescription).HasMaxLength(128).IsRequired();
            b.Property(p => p.WorkContent).HasMaxLength(1024);
            b.Property(p => p.LastTriggeredAt).HasColumnType("timestamptz");
            b.Property(p => p.CreatedAt).HasColumnType("timestamptz");
        });

        // ── maintenance_work_orders 表 ──
        modelBuilder.Entity<MaintenanceWorkOrder>(b =>
        {
            b.ToTable("maintenance_work_orders");
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).HasConversion<UlidToGuidConverter>();
            b.Property(o => o.OrderNumber).HasMaxLength(32).IsRequired();
            b.HasIndex(o => o.OrderNumber).IsUnique();
            b.Property(o => o.MaintenancePlanId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(o => o.MaintenancePlanId).HasDatabaseName("idx_mt_order_plan");
            b.Property(o => o.EquipmentCode).HasMaxLength(32).IsRequired();
            b.HasIndex(o => o.EquipmentCode).HasDatabaseName("idx_mt_order_equipment");
            b.Property(o => o.EquipmentName).HasMaxLength(64).IsRequired();
            b.Property(o => o.MaintenanceType).HasConversion<int>();
            b.Property(o => o.TriggerType).HasConversion<int>();
            b.Property(o => o.Title).HasMaxLength(128).IsRequired();
            b.Property(o => o.Description).HasMaxLength(1024);
            b.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
            b.HasIndex(o => o.Status).HasDatabaseName("idx_mt_order_status");
            b.Property(o => o.AssignedTo).HasMaxLength(32);
            b.Property(o => o.CompletedBy).HasMaxLength(32);
            b.Property(o => o.CompletionRemarks).HasMaxLength(512);
            b.Property(o => o.CompletedAt).HasColumnType("timestamptz");
            b.Property(o => o.CreatedAt).HasColumnType("timestamptz");
        });

        // ═══════════════════════════════════════════════════════════
        //  T2.18 备件管理
        // ═══════════════════════════════════════════════════════════

        // ── spare_parts 表 ──
        modelBuilder.Entity<SparePart>(b =>
        {
            b.ToTable("spare_parts");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).HasConversion<UlidToGuidConverter>();
            b.Property(p => p.MaterialCode).HasMaxLength(32).IsRequired();
            b.HasIndex(p => p.MaterialCode).IsUnique();
            b.Property(p => p.MaterialName).HasMaxLength(64).IsRequired();
            b.Property(p => p.Specification).HasMaxLength(128).IsRequired();
            b.Property(p => p.Unit).HasMaxLength(16).IsRequired();
            b.Property(p => p.EquipmentCode).HasMaxLength(32);
            b.HasIndex(p => p.EquipmentCode).HasDatabaseName("idx_spare_part_equipment");
            b.Property(p => p.Remarks).HasMaxLength(256);
            b.Property(p => p.CreatedAt).HasColumnType("timestamptz");
            b.Property(p => p.UpdatedAt).HasColumnType("timestamptz");
        });

        // ── spare_part_usages 表 ──
        modelBuilder.Entity<SparePartUsage>(b =>
        {
            b.ToTable("spare_part_usages");
            b.HasKey(u => u.Id);
            b.Property(u => u.Id).HasConversion<UlidToGuidConverter>();
            b.Property(u => u.SparePartId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(u => u.SparePartId).HasDatabaseName("idx_spare_usage_part");
            b.Property(u => u.MaintenanceWorkOrderId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(u => u.MaintenanceWorkOrderId).HasDatabaseName("idx_spare_usage_order");
            b.HasIndex(u => new { u.SparePartId, u.MaintenanceWorkOrderId }).HasDatabaseName("idx_spare_usage_composite");
            b.Property(u => u.Remarks).HasMaxLength(256);
            b.Property(u => u.CreatedAt).HasColumnType("timestamptz");
        });

        // ── purchase_requests 表 ──
        modelBuilder.Entity<PurchaseRequest>(b =>
        {
            b.ToTable("purchase_requests");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasConversion<UlidToGuidConverter>();
            b.Property(r => r.RequestNumber).HasMaxLength(32).IsRequired();
            b.HasIndex(r => r.RequestNumber).IsUnique();
            b.Property(r => r.SparePartId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(r => r.SparePartId).HasDatabaseName("idx_purchase_request_part");
            b.Property(r => r.Reason).HasMaxLength(256).IsRequired();
            b.Property(r => r.Status).HasMaxLength(16).IsRequired();
            b.HasIndex(r => r.Status).HasDatabaseName("idx_purchase_request_status");
            b.Property(r => r.RequestedBy).HasMaxLength(32).IsRequired();
            b.Property(r => r.ApprovedBy).HasMaxLength(32);
            b.Property(r => r.ApprovedAt).HasColumnType("timestamptz");
            b.Property(r => r.CreatedAt).HasColumnType("timestamptz");
            b.Property(r => r.UpdatedAt).HasColumnType("timestamptz");
        });

        // ═══════════════════════════════════════════════════════════
        //  T3.1/T3.2 M07 工艺管理 — 工艺路线
        // ═══════════════════════════════════════════════════════════

        // ── routings 表 ──
        modelBuilder.Entity<Routing>(b =>
        {
            b.ToTable("routings");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasConversion<UlidToGuidConverter>();
            b.Property(r => r.ProductCode).HasMaxLength(32).IsRequired();
            b.HasIndex(r => r.ProductCode).HasDatabaseName("idx_routing_product");
            b.Property(r => r.Name).HasMaxLength(128).IsRequired();
            b.Property(r => r.Version).HasMaxLength(16).IsRequired();
            b.HasIndex(r => new { r.ProductCode, r.Version }).IsUnique().HasDatabaseName("ux_routing_product_version");
            b.Property(r => r.EcoNumber).HasMaxLength(32);
            b.Property(r => r.EcoStatus).HasConversion<int>();
            b.HasIndex(r => r.EcoStatus).HasDatabaseName("idx_routing_eco_status");
            b.Property(r => r.IsActive).HasDefaultValue(false);
            b.HasIndex(r => r.IsActive).HasDatabaseName("idx_routing_active");
            b.Property(r => r.ChangeDescription).HasMaxLength(1024);
            b.Property(r => r.CreatedBy).HasMaxLength(32).IsRequired();
            b.Property(r => r.ApprovedBy).HasMaxLength(32);
            b.Property(r => r.ApprovedAt).HasColumnType("timestamptz");
            b.Property(r => r.CreatedAt).HasColumnType("timestamptz");
            b.Property(r => r.UpdatedAt).HasColumnType("timestamptz");
            b.Property(r => r.EffectiveDate).HasColumnType("timestamptz");
            b.Property(r => r.ExpirationDate).HasColumnType("timestamptz");                // 工序定义作为 JSONB 嵌入（包含 31 工序 + 参数模板）
            // 注意：EF Core 要求仅在最外层拥有实体调用 ToJson()，
            // 嵌套的 OwnsMany 自动成为同一 JSON 文档的一部分。
            b.OwnsMany(r => r.Operations, op =>
            {
                op.ToJson();
                op.Property(x => x.OperationCode).HasMaxLength(32).IsRequired();
                op.Property(x => x.OperationName).HasMaxLength(64).IsRequired();
                op.Property(x => x.FixtureCode).HasMaxLength(32);
                op.Property(x => x.FixtureName).HasMaxLength(64);

                // 参数模板嵌套（不调用 ToJson，自动属于 Operations JSON）
                op.OwnsMany(x => x.ParameterTemplates, pt =>
                {
                    pt.Property(p => p.ParameterCode).HasMaxLength(32).IsRequired();
                    pt.Property(p => p.ParameterName).HasMaxLength(64).IsRequired();
                    pt.Property(p => p.Unit).HasMaxLength(16).IsRequired();
                });
            });
        });

        // ═══════════════════════════════════════════════════════════
        //  M09 排程管理 (T3.10-T3.13)
        // ═══════════════════════════════════════════════════════════

        // ── production_schedules 表 ──
        modelBuilder.Entity<ProductionSchedule>(b =>
        {
            b.ToTable("production_schedules");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasConversion<UlidToGuidConverter>();
            b.Property(s => s.OrderId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(s => s.OrderId).HasDatabaseName("idx_schedule_order");
            b.Property(s => s.OrderNumber).HasMaxLength(32).IsRequired();
            b.Property(s => s.ProductCode).HasMaxLength(32).IsRequired();
            b.Property(s => s.EquipmentCode).HasMaxLength(32).IsRequired();
            b.HasIndex(s => s.EquipmentCode).HasDatabaseName("idx_schedule_equipment");
            b.HasIndex(s => s.ScheduleDate).HasDatabaseName("idx_schedule_date");
            b.Property(s => s.Status).HasConversion<int>();
            b.HasIndex(s => s.Status).HasDatabaseName("idx_schedule_status");
            b.Property(s => s.RushType).HasConversion<int>();
            b.Property(s => s.RushReason).HasMaxLength(256);
            b.Property(s => s.Remarks).HasMaxLength(512);
            b.Property(s => s.PlannedStartAt).HasColumnType("timestamptz");
            b.Property(s => s.PlannedEndAt).HasColumnType("timestamptz");
            b.Property(s => s.CreatedAt).HasColumnType("timestamptz");
            b.Property(s => s.UpdatedAt).HasColumnType("timestamptz");
        });

        // ── capacity_calendars 表 ──
        modelBuilder.Entity<CapacityCalendar>(b =>
        {
            b.ToTable("capacity_calendars");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).HasConversion<UlidToGuidConverter>();
            b.Property(c => c.EquipmentCode).HasMaxLength(32).IsRequired();
            b.HasIndex(c => c.EquipmentCode).IsUnique();
            b.Property(c => c.EquipmentName).HasMaxLength(64).IsRequired();
            b.Property(c => c.ShiftTemplate).HasMaxLength(256).IsRequired();
            b.Property(c => c.CreatedAt).HasColumnType("timestamptz");
            b.Property(c => c.UpdatedAt).HasColumnType("timestamptz");
        });

        // ═══════════════════════════════════════════════════════════
        //  M08 SQE 供应商质量 (T3.6-T3.8)
        // ═══════════════════════════════════════════════════════════

        // ── suppliers 表 ──
        modelBuilder.Entity<Supplier>(b =>
        {
            b.ToTable("suppliers");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasConversion<UlidToGuidConverter>();
            b.Property(s => s.SupplierCode).HasMaxLength(32).IsRequired();
            b.HasIndex(s => s.SupplierCode).IsUnique();
            b.Property(s => s.SupplierName).HasMaxLength(128).IsRequired();
            b.Property(s => s.ShortName).HasMaxLength(64);
            b.Property(s => s.CreditCode).HasMaxLength(32);
            b.Property(s => s.ContactPerson).HasMaxLength(32);
            b.Property(s => s.ContactPhone).HasMaxLength(32);
            b.Property(s => s.ContactEmail).HasMaxLength(64);
            b.Property(s => s.Address).HasMaxLength(256);
            b.Property(s => s.MaterialCategory).HasMaxLength(64).IsRequired();
            b.HasIndex(s => s.MaterialCategory).HasDatabaseName("idx_supplier_category");
            b.Property(s => s.MaterialCodes).HasMaxLength(512).IsRequired();
            b.Property(s => s.Tier).HasConversion<int>();
            b.HasIndex(s => s.Tier).HasDatabaseName("idx_supplier_tier");
            b.Property(s => s.IsoCertification).HasMaxLength(64);
            b.Property(s => s.IsoExpiryDate).HasColumnType("timestamptz");
            b.Property(s => s.Remarks).HasMaxLength(512);
            b.Property(s => s.LatestScoreAt).HasColumnType("timestamptz");
            b.Property(s => s.CreatedAt).HasColumnType("timestamptz");
            b.Property(s => s.UpdatedAt).HasColumnType("timestamptz");
        });

        // ── supplier_score_cards 表 ──
        modelBuilder.Entity<SupplierScoreCard>(b =>
        {
            b.ToTable("supplier_score_cards");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).HasConversion<UlidToGuidConverter>();
            b.Property(c => c.SupplierId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(c => c.SupplierId).HasDatabaseName("idx_scorecard_supplier");
            b.Property(c => c.SupplierCode).HasMaxLength(32).IsRequired();
            b.Property(c => c.Period).HasMaxLength(16).IsRequired();
            b.HasIndex(c => c.Period).HasDatabaseName("idx_scorecard_period");
            b.HasIndex(c => new { c.SupplierId, c.Period }).HasDatabaseName("ux_scorecard_supplier_period");
            b.Property(c => c.IncomingQualityData).HasMaxLength(256);
            b.Property(c => c.OnTimeDeliveryData).HasMaxLength(256);
            b.Property(c => c.EightDResponseData).HasMaxLength(256);
            b.Property(c => c.PpapPassRateData).HasMaxLength(256);
            b.Property(c => c.PriceCompetitivenessData).HasMaxLength(256);
            b.Property(c => c.EvaluatedBy).HasMaxLength(32).IsRequired();
            b.Property(c => c.Remarks).HasMaxLength(512);
            b.Property(c => c.CreatedAt).HasColumnType("timestamptz");
        });

        // ── ppap_documents 表 ──
        modelBuilder.Entity<PpapDocument>(b =>
        {
            b.ToTable("ppap_documents");
            b.HasKey(d => d.Id);
            b.Property(d => d.Id).HasConversion<UlidToGuidConverter>();
            b.Property(d => d.SupplierId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(d => d.SupplierId).HasDatabaseName("idx_ppap_supplier");
            b.Property(d => d.SupplierCode).HasMaxLength(32).IsRequired();
            b.Property(d => d.MaterialCode).HasMaxLength(32).IsRequired();
            b.HasIndex(d => d.MaterialCode).HasDatabaseName("idx_ppap_material");
            b.Property(d => d.MaterialName).HasMaxLength(64).IsRequired();
            b.Property(d => d.Status).HasConversion<int>();
            b.HasIndex(d => d.Status).HasDatabaseName("idx_ppap_status");
            b.Property(d => d.SubmittedAt).HasColumnType("timestamptz");
            b.Property(d => d.ApprovedAt).HasColumnType("timestamptz");
            b.Property(d => d.ExpiryDate).HasColumnType("timestamptz");
            b.Property(d => d.Version).HasMaxLength(16);
            b.Property(d => d.ApprovedBy).HasMaxLength(32);
            b.Property(d => d.RejectionReason).HasMaxLength(512);
            b.Property(d => d.Remarks).HasMaxLength(512);
            b.Property(d => d.CreatedBy).HasMaxLength(32).IsRequired();
            b.Property(d => d.CreatedAt).HasColumnType("timestamptz");
            b.Property(d => d.UpdatedAt).HasColumnType("timestamptz");
        });

        // ── critical_supplier_settings 表 ──
        modelBuilder.Entity<CriticalSupplierSetting>(b =>
        {
            b.ToTable("critical_supplier_settings");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasConversion<UlidToGuidConverter>();
            b.Property(s => s.MaterialCode).HasMaxLength(32).IsRequired();
            b.HasIndex(s => s.MaterialCode).IsUnique();
            b.Property(s => s.MaterialName).HasMaxLength(64).IsRequired();
            b.Property(s => s.Remarks).HasMaxLength(256);
            b.Property(s => s.CreatedAt).HasColumnType("timestamptz");
            b.Property(s => s.UpdatedAt).HasColumnType("timestamptz");
        });

        // ═══════════════════════════════════════════════════════════
        //  T2.6 100% 在线液压测试结果
        // ═══════════════════════════════════════════════════════════

        // ── hydraulic_test_results 表 ──
        modelBuilder.Entity<HydraulicTestResult>(b =>
        {
            b.ToTable("hydraulic_test_results");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasConversion<UlidToGuidConverter>();
            b.Property(r => r.EquipmentCode).HasMaxLength(32).IsRequired();
            b.HasIndex(r => r.EquipmentCode).HasDatabaseName("idx_hydraulic_equipment");
            b.Property(r => r.OrderId).HasConversion<UlidToGuidConverter>();
            b.HasIndex(r => r.OrderId).HasDatabaseName("idx_hydraulic_order");
            b.Property(r => r.ProductSerial).HasMaxLength(64);
            b.HasIndex(r => r.ProductSerial).HasDatabaseName("idx_hydraulic_serial");
            b.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);
            b.HasIndex(r => r.Status).HasDatabaseName("idx_hydraulic_status");
            b.Property(r => r.FailureReason).HasMaxLength(512);
            b.Property(r => r.UnlockedBy).HasMaxLength(32);
            b.Property(r => r.UnlockedAt).HasColumnType("timestamptz");
            b.Property(r => r.StartedAt).HasColumnType("timestamptz");
            b.Property(r => r.CompletedAt).HasColumnType("timestamptz");
            b.Property(r => r.CreatedAt).HasColumnType("timestamptz");

            // 电磁阀测试结果作为 JSONB 存储
            b.OwnsMany(r => r.SolenoidTests, s =>
            {
                s.ToJson();
                s.Property(x => x.FaultCode).HasMaxLength(16);
            });
        });
    }
}

/// <summary>
/// Ulid → Guid 转换器（ValueConverter）。
/// Ulid 128-bit 可排序 UUID，前 48 bit 时间戳 → B+Tree 友好。
/// 禁止 Guid.NewGuid / 自增 ID（分布式不安全 / 索引碎片）。
/// </summary>
public class UlidToGuidConverter : ValueConverter<Ulid, Guid>
{
    public UlidToGuidConverter()
        : base(
            ulid => ulid.ToGuid(),
            guid => new Ulid(guid))
    {
    }
}
