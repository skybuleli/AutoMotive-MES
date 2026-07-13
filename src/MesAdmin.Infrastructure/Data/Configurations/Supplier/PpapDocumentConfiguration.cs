using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Supplier;

/// <summary>PPAP 文档实体配置。</summary>
public sealed class PpapDocumentConfiguration : IEntityTypeConfiguration<PpapDocument>
{
    public void Configure(EntityTypeBuilder<PpapDocument> builder)
    {
        builder.ToTable("ppap_documents");
        builder.HasKey(d => d.Id);
        builder.HasIndex(d => d.SupplierId).HasDatabaseName("idx_ppap_supplier");
        builder.Property(d => d.SupplierCode).HasMaxLength(32).IsRequired();
        builder.Property(d => d.MaterialCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(d => d.MaterialCode).HasDatabaseName("idx_ppap_material");
        builder.Property(d => d.MaterialName).HasMaxLength(64).IsRequired();
        builder.Property(d => d.Status).HasConversion<int>();
        builder.HasIndex(d => d.Status).HasDatabaseName("idx_ppap_status");
        builder.Property(d => d.SubmittedAt).HasColumnType("timestamptz");
        builder.Property(d => d.ApprovedAt).HasColumnType("timestamptz");
        builder.Property(d => d.ExpiryDate).HasColumnType("timestamptz");
        builder.Property(d => d.Version).HasMaxLength(16);
        builder.Property(d => d.ApprovedBy).HasMaxLength(32);
        builder.Property(d => d.RejectionReason).HasMaxLength(512);
        builder.Property(d => d.Remarks).HasMaxLength(512);
        builder.Property(d => d.CreatedBy).HasMaxLength(32).IsRequired();
        builder.Property(d => d.CreatedAt).HasColumnType("timestamptz");
        builder.Property(d => d.UpdatedAt).HasColumnType("timestamptz");
    }
}
