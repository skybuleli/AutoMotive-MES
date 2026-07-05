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
