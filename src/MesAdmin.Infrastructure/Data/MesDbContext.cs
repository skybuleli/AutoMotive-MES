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
            guid => new Ulid(guid),
            convertsNulls: false)
    {
    }
}
