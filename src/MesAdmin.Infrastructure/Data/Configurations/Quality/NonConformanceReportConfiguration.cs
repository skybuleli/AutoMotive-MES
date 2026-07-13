using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Quality;

/// <summary>不合格品报告实体配置。</summary>
public sealed class NonConformanceReportConfiguration : IEntityTypeConfiguration<NonConformanceReport>
{
    public void Configure(EntityTypeBuilder<NonConformanceReport> builder)
    {
        builder.ToTable("non_conformance_reports");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.NcrNumber).HasMaxLength(32).IsRequired();
        builder.HasIndex(n => n.NcrNumber).IsUnique();
        builder.HasIndex(n => n.OrderId).HasDatabaseName("idx_ncr_order");
        builder.Property(n => n.OrderNumber).HasMaxLength(32);
        builder.Property(n => n.ProductCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(n => n.ProductCode).HasDatabaseName("idx_ncr_product");
        builder.Property(n => n.ProductName).HasMaxLength(64).IsRequired();
        builder.Property(n => n.BatchNumber).HasMaxLength(64);
        builder.HasIndex(n => n.BatchNumber).HasDatabaseName("idx_ncr_batch");
        builder.Property(n => n.DiscoveredAt).HasConversion<string>().HasMaxLength(16);
        builder.Property(n => n.Description).HasMaxLength(1024).IsRequired();
        builder.Property(n => n.Severity).HasConversion<string>().HasMaxLength(16);
        builder.Property(n => n.Status).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(n => n.Status).HasDatabaseName("idx_ncr_status");
        builder.Property(n => n.Disposition).HasConversion<string>().HasMaxLength(16);
        builder.Property(n => n.ResponsibleDept).HasMaxLength(32);
        builder.Property(n => n.DiscoveredBy).HasMaxLength(32).IsRequired();
        builder.Property(n => n.ReviewerId).HasMaxLength(32);
        builder.Property(n => n.ReviewComments).HasMaxLength(512);
        builder.Property(n => n.DispositionDeadline).HasColumnType("timestamptz");
        builder.Property(n => n.CloseRemarks).HasMaxLength(512);
        builder.Property(n => n.CreatedAt).HasColumnType("timestamptz");
        builder.Property(n => n.ClosedAt).HasColumnType("timestamptz");
        builder.HasIndex(n => new { n.ProductCode, n.CreatedAt }).HasDatabaseName("idx_ncr_product_created");
    }
}
