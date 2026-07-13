using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Quality;

/// <summary>质量记录实体配置。</summary>
public sealed class QualityRecordConfiguration : IEntityTypeConfiguration<QualityRecord>
{
    public void Configure(EntityTypeBuilder<QualityRecord> builder)
    {
        builder.ToTable("quality_records");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.OrderId).HasDatabaseName("idx_quality_order");
        builder.Property(r => r.Stage).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(r => r.Stage).HasDatabaseName("idx_quality_stage");
        builder.Property(r => r.ProductCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(r => r.ProductCode).HasDatabaseName("idx_quality_product");
        builder.Property(r => r.BatchNumber).HasMaxLength(64);
        builder.HasIndex(r => r.BatchNumber).HasDatabaseName("idx_quality_batch");
        builder.Property(r => r.InspectionPlanName).HasMaxLength(64).IsRequired();
        builder.Property(r => r.AqlScheme).HasMaxLength(32);
        builder.Property(r => r.InspectorId).HasMaxLength(32).IsRequired();
        builder.Property(r => r.Verdict).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.SupplierCode).HasMaxLength(32);
        builder.Property(r => r.SupplierName).HasMaxLength(64);
        builder.Property(r => r.OrderNumber).HasMaxLength(32);
        builder.Property(r => r.ProductName).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Remarks).HasMaxLength(512);
        builder.Property(r => r.CreatedAt).HasColumnType("timestamptz");
        builder.Property(r => r.CompletedAt).HasColumnType("timestamptz");

        builder.HasIndex(r => new { r.Stage, r.CreatedAt }).HasDatabaseName("idx_quality_stage_created");

        builder.OwnsMany(r => r.Characteristics, p =>
        {
            p.ToJson();
            p.Property(c => c.CharacteristicCode).HasMaxLength(32).IsRequired();
            p.Property(c => c.CharacteristicName).HasMaxLength(64).IsRequired();
            p.Property(c => c.Unit).HasMaxLength(16).IsRequired();
            p.Property(c => c.MeasurementTool).HasMaxLength(32);
        });
    }
}
