using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Quality;

/// <summary>SPC 样本实体配置。</summary>
public sealed class SpcSampleConfiguration : IEntityTypeConfiguration<SpcSample>
{
    public void Configure(EntityTypeBuilder<SpcSample> builder)
    {
        builder.ToTable("spc_samples");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.CharacteristicCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(s => s.CharacteristicCode).HasDatabaseName("idx_spc_characteristic");
        builder.HasIndex(s => s.OrderId).HasDatabaseName("idx_spc_order");
        builder.Property(s => s.OrderNumber).HasMaxLength(32);
        builder.Property(s => s.EquipmentCode).HasMaxLength(32);
        builder.HasIndex(s => s.EquipmentCode).HasDatabaseName("idx_spc_equipment");
        builder.HasIndex(s => s.SubgroupIndex).HasDatabaseName("idx_spc_subgroup");
        builder.HasIndex(s => new { s.CharacteristicCode, s.SubgroupIndex }).IsUnique();
        builder.Property(s => s.Source).HasMaxLength(8);
        builder.Property(s => s.GaugeId).IsRequired(false);
        builder.HasIndex(s => s.GaugeId).HasDatabaseName("idx_spc_gauge");
        builder.Property(s => s.Values).HasColumnType("jsonb");
        builder.Property(s => s.CollectedAt).HasColumnType("timestamptz");
        builder.HasIndex(s => s.CollectedAt).HasDatabaseName("idx_spc_collected_at");
        builder.HasIndex(s => new { s.CharacteristicCode, s.CollectedAt }).HasDatabaseName("idx_spc_char_collected");
    }
}
