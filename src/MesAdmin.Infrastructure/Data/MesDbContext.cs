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
            b.HasIndex(o => o.Status).HasDatabaseName("idx_orders_status");
            b.Property(o => o.CreatedAt).HasColumnType("timestamptz");
            b.Property(o => o.CompletedAt).HasColumnType("timestamptz");
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
