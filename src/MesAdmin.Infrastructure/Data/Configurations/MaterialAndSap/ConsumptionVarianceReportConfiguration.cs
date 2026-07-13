using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.MaterialAndSap;

/// <summary>物料消耗差异报告实体配置。</summary>
public sealed class ConsumptionVarianceReportConfiguration : IEntityTypeConfiguration<ConsumptionVarianceReport>
{
    public void Configure(EntityTypeBuilder<ConsumptionVarianceReport> builder)
    {
        builder.ToTable("consumption_variance_reports");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.OrderId).HasDatabaseName("idx_variance_order");
        builder.Property(r => r.OrderNumber).HasMaxLength(32).IsRequired();
        builder.Property(r => r.MaterialCode).HasMaxLength(32).IsRequired();
        builder.Property(r => r.MaterialName).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Direction).HasMaxLength(8).IsRequired();
        builder.Property(r => r.Unit).HasMaxLength(16).IsRequired();
        builder.Property(r => r.ResolvedBy).HasMaxLength(32);
        builder.Property(r => r.Resolution).HasMaxLength(512);
        builder.Property(r => r.CreatedAt).HasColumnType("timestamptz");
        builder.Property(r => r.ResolvedAt).HasColumnType("timestamptz");
    }
}
