using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Equipment;

/// <summary>备件使用记录实体配置。</summary>
public sealed class SparePartUsageConfiguration : IEntityTypeConfiguration<SparePartUsage>
{
    public void Configure(EntityTypeBuilder<SparePartUsage> builder)
    {
        builder.ToTable("spare_part_usages");
        builder.HasKey(u => u.Id);
        builder.HasIndex(u => u.SparePartId).HasDatabaseName("idx_spare_usage_part");
        builder.HasIndex(u => u.MaintenanceWorkOrderId).HasDatabaseName("idx_spare_usage_order");
        builder.HasIndex(u => new { u.SparePartId, u.MaintenanceWorkOrderId }).HasDatabaseName("idx_spare_usage_composite");
        builder.Property(u => u.Remarks).HasMaxLength(256);
        builder.Property(u => u.CreatedAt).HasColumnType("timestamptz");
    }
}
