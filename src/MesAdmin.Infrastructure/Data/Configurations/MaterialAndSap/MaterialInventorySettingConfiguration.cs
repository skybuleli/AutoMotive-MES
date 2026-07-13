using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.MaterialAndSap;

/// <summary>物料库存阈值实体配置。</summary>
public sealed class MaterialInventorySettingConfiguration : IEntityTypeConfiguration<MaterialInventorySetting>
{
    public void Configure(EntityTypeBuilder<MaterialInventorySetting> builder)
    {
        builder.ToTable("material_inventory_settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.MaterialCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(s => s.MaterialCode).HasDatabaseName("idx_inv_setting_material");
        builder.Property(s => s.MaterialName).HasMaxLength(64).IsRequired();
        builder.Property(s => s.StationId).HasMaxLength(32);
        builder.HasIndex(s => s.StationId).HasDatabaseName("idx_inv_setting_station");
        builder.Property(s => s.Unit).HasMaxLength(16).IsRequired();
        builder.Property(s => s.UpdatedBy).HasMaxLength(32);
        builder.Property(s => s.CreatedAt).HasColumnType("timestamptz");
        builder.Property(s => s.UpdatedAt).HasColumnType("timestamptz");
    }
}
