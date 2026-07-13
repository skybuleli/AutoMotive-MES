using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Equipment;

/// <summary>备件主数据实体配置。</summary>
public sealed class SparePartConfiguration : IEntityTypeConfiguration<SparePart>
{
    public void Configure(EntityTypeBuilder<SparePart> builder)
    {
        builder.ToTable("spare_parts");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.MaterialCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(p => p.MaterialCode).IsUnique();
        builder.Property(p => p.MaterialName).HasMaxLength(64).IsRequired();
        builder.Property(p => p.Specification).HasMaxLength(128).IsRequired();
        builder.Property(p => p.Unit).HasMaxLength(16).IsRequired();
        builder.Property(p => p.EquipmentCode).HasMaxLength(32);
        builder.HasIndex(p => p.EquipmentCode).HasDatabaseName("idx_spare_part_equipment");
        builder.Property(p => p.Remarks).HasMaxLength(256);
        builder.Property(p => p.CreatedAt).HasColumnType("timestamptz");
        builder.Property(p => p.UpdatedAt).HasColumnType("timestamptz");
    }
}
