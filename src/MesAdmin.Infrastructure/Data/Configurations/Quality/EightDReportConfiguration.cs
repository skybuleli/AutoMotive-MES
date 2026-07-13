using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Quality;

/// <summary>8D 报告实体配置。</summary>
public sealed class EightDReportConfiguration : IEntityTypeConfiguration<EightDReport>
{
    public void Configure(EntityTypeBuilder<EightDReport> builder)
    {
        builder.ToTable("eight_d_reports");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ReportNumber).HasMaxLength(32).IsRequired();
        builder.HasIndex(r => r.ReportNumber).IsUnique();
        builder.Property(r => r.NcrNumber).HasMaxLength(32);
        builder.Property(r => r.Title).HasMaxLength(128).IsRequired();
        builder.Property(r => r.ProductCode).HasMaxLength(32).IsRequired();
        builder.Property(r => r.ProductName).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.TeamLeader).HasMaxLength(32);
        builder.Property(r => r.TeamMembers).HasMaxLength(256);
        builder.Property(r => r.ProblemDescription).HasMaxLength(2048);
        builder.Property(r => r.ContainmentAction).HasMaxLength(1024);
        builder.Property(r => r.ContainmentDate).HasColumnType("timestamptz");
        builder.Property(r => r.RootCauseAnalysis).HasMaxLength(2048);
        builder.Property(r => r.RootCause).HasMaxLength(512);
        builder.Property(r => r.CorrectiveAction).HasMaxLength(2048);
        builder.Property(r => r.CorrectiveActionOwner).HasMaxLength(32);
        builder.Property(r => r.CorrectiveActionDueDate).HasColumnType("timestamptz");
        builder.Property(r => r.VerificationMethod).HasMaxLength(512);
        builder.Property(r => r.VerificationResult).HasMaxLength(512);
        builder.Property(r => r.VerificationDate).HasColumnType("timestamptz");
        builder.Property(r => r.PreventiveAction).HasMaxLength(2048);
        builder.Property(r => r.Summary).HasMaxLength(1024);
        builder.Property(r => r.ClosedAt).HasColumnType("timestamptz");
        builder.Property(r => r.CreatedAt).HasColumnType("timestamptz");
        builder.Property(r => r.UpdatedAt).HasColumnType("timestamptz");
        builder.HasIndex(r => new { r.ProductCode, r.CreatedAt }).HasDatabaseName("idx_eightd_product_created");
    }
}
