using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Production;

/// <summary>生产工单实体配置。</summary>
public sealed class ProductionOrderConfiguration : IEntityTypeConfiguration<ProductionOrder>
{
    public void Configure(EntityTypeBuilder<ProductionOrder> builder)
    {
        builder.ToTable("production_orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.OrderNumber).HasMaxLength(32).IsRequired();
        builder.HasIndex(o => o.OrderNumber).IsUnique();
        builder.Property(o => o.ProductCode).HasMaxLength(32).IsRequired();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(o => o.BomVersion).HasMaxLength(32);
        builder.Property(o => o.WorkCenterId).HasMaxLength(32);
        builder.Property(o => o.Shift).HasMaxLength(16);
        builder.Property(o => o.SourceSystem).HasMaxLength(32);
        builder.Property(o => o.ExternalOrderNumber).HasMaxLength(64);
        builder.Property(o => o.CancelReason).HasMaxLength(256);
        builder.HasIndex(o => o.Status).HasDatabaseName("idx_orders_status");
        builder.HasIndex(o => o.CreatedAt).HasDatabaseName("idx_orders_created_at");
        builder.Property(o => o.CreatedAt).HasColumnType("timestamptz");
        builder.Property(o => o.CompletedAt).HasColumnType("timestamptz");
        builder.Property(o => o.PlannedStartAt).HasColumnType("timestamptz");
        builder.Property(o => o.PlannedEndAt).HasColumnType("timestamptz");
        builder.Property(o => o.ActualStartAt).HasColumnType("timestamptz");
        builder.Property(o => o.ActualEndAt).HasColumnType("timestamptz");
    }
}
