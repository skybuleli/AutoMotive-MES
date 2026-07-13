using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupplierEntity = MesAdmin.Domain.Models.Supplier;

namespace MesAdmin.Infrastructure.Data.Configurations.Supplier;

/// <summary>供应商主数据实体配置。</summary>
public sealed class SupplierConfiguration : IEntityTypeConfiguration<SupplierEntity>
{
    public void Configure(EntityTypeBuilder<SupplierEntity> builder)
    {
        builder.ToTable("suppliers");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.SupplierCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(s => s.SupplierCode).IsUnique();
        builder.Property(s => s.SupplierName).HasMaxLength(128).IsRequired();
        builder.Property(s => s.ShortName).HasMaxLength(64);
        builder.Property(s => s.CreditCode).HasMaxLength(32);
        builder.Property(s => s.ContactPerson).HasMaxLength(32);
        builder.Property(s => s.ContactPhone).HasMaxLength(32);
        builder.Property(s => s.ContactEmail).HasMaxLength(64);
        builder.Property(s => s.Address).HasMaxLength(256);
        builder.Property(s => s.MaterialCategory).HasMaxLength(64).IsRequired();
        builder.HasIndex(s => s.MaterialCategory).HasDatabaseName("idx_supplier_category");
        builder.Property(s => s.MaterialCodes).HasMaxLength(512).IsRequired();
        builder.Property(s => s.Tier).HasConversion<int>();
        builder.HasIndex(s => s.Tier).HasDatabaseName("idx_supplier_tier");
        builder.Property(s => s.IsoCertification).HasMaxLength(64);
        builder.Property(s => s.IsoExpiryDate).HasColumnType("timestamptz");
        builder.Property(s => s.Remarks).HasMaxLength(512);
        builder.Property(s => s.LatestScoreAt).HasColumnType("timestamptz");
        builder.Property(s => s.CreatedAt).HasColumnType("timestamptz");
        builder.Property(s => s.UpdatedAt).HasColumnType("timestamptz");
    }
}
