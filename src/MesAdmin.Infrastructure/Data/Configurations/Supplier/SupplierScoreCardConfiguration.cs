using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Supplier;

/// <summary>供应商记分卡实体配置。</summary>
public sealed class SupplierScoreCardConfiguration : IEntityTypeConfiguration<SupplierScoreCard>
{
    public void Configure(EntityTypeBuilder<SupplierScoreCard> builder)
    {
        builder.ToTable("supplier_score_cards");
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => c.SupplierId).HasDatabaseName("idx_scorecard_supplier");
        builder.Property(c => c.SupplierCode).HasMaxLength(32).IsRequired();
        builder.Property(c => c.Period).HasMaxLength(16).IsRequired();
        builder.HasIndex(c => c.Period).HasDatabaseName("idx_scorecard_period");
        builder.HasIndex(c => new { c.SupplierId, c.Period }).HasDatabaseName("ux_scorecard_supplier_period");
        builder.Property(c => c.IncomingQualityData).HasMaxLength(256);
        builder.Property(c => c.OnTimeDeliveryData).HasMaxLength(256);
        builder.Property(c => c.EightDResponseData).HasMaxLength(256);
        builder.Property(c => c.PpapPassRateData).HasMaxLength(256);
        builder.Property(c => c.PriceCompetitivenessData).HasMaxLength(256);
        builder.Property(c => c.EvaluatedBy).HasMaxLength(32).IsRequired();
        builder.Property(c => c.Remarks).HasMaxLength(512);
        builder.Property(c => c.CreatedAt).HasColumnType("timestamptz");
    }
}
