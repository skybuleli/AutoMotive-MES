using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.MaterialAndSap;

/// <summary>来料批次实体配置。</summary>
public sealed class MaterialBatchConfiguration : IEntityTypeConfiguration<MaterialBatch>
{
    public void Configure(EntityTypeBuilder<MaterialBatch> builder)
    {
        builder.ToTable("material_batches");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.MaterialCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(b => b.MaterialCode).HasDatabaseName("idx_material_code");
        builder.Property(b => b.MaterialName).HasMaxLength(64).IsRequired();
        builder.Property(b => b.BatchNumber).HasMaxLength(64).IsRequired();
        builder.HasIndex(b => b.BatchNumber).IsUnique();
        builder.Property(b => b.SupplierCode).HasMaxLength(32).IsRequired();
        builder.Property(b => b.SupplierName).HasMaxLength(64).IsRequired();
        builder.Property(b => b.Unit).HasMaxLength(16).IsRequired();
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(b => b.Status).HasDatabaseName("idx_material_status");
        builder.Property(b => b.ProductionDate).HasColumnType("timestamptz");
        builder.Property(b => b.ReceivedAt).HasColumnType("timestamptz");
    }
}
