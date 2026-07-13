using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.MaterialAndSap;

/// <summary>库存预警实体配置。</summary>
public sealed class InventoryAlertConfiguration : IEntityTypeConfiguration<InventoryAlert>
{
    public void Configure(EntityTypeBuilder<InventoryAlert> builder)
    {
        builder.ToTable("inventory_alerts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.MaterialCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(a => a.MaterialCode).HasDatabaseName("idx_inv_alert_material");
        builder.Property(a => a.MaterialName).HasMaxLength(64).IsRequired();
        builder.Property(a => a.StationId).HasMaxLength(32);
        builder.Property(a => a.AlertLevel).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(a => a.AlertLevel).HasDatabaseName("idx_inv_alert_level");
        builder.Property(a => a.ResolvedBy).HasMaxLength(32);
        builder.Property(a => a.Resolution).HasMaxLength(256);
        builder.Property(a => a.CreatedAt).HasColumnType("timestamptz");
        builder.Property(a => a.ResolvedAt).HasColumnType("timestamptz");
    }
}
