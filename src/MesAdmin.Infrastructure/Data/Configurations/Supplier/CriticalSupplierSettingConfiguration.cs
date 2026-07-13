using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Supplier;

/// <summary>关键供应商设置实体配置。</summary>
public sealed class CriticalSupplierSettingConfiguration : IEntityTypeConfiguration<CriticalSupplierSetting>
{
    public void Configure(EntityTypeBuilder<CriticalSupplierSetting> builder)
    {
        builder.ToTable("critical_supplier_settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.MaterialCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(s => s.MaterialCode).IsUnique();
        builder.Property(s => s.MaterialName).HasMaxLength(64).IsRequired();
        builder.Property(s => s.Remarks).HasMaxLength(256);
        builder.Property(s => s.CreatedAt).HasColumnType("timestamptz");
        builder.Property(s => s.UpdatedAt).HasColumnType("timestamptz");
    }
}
