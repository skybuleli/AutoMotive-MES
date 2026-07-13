using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Production;

/// <summary>入库单实体配置。</summary>
public sealed class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> builder)
    {
        builder.ToTable("goods_receipts");
        builder.HasKey(g => g.Id);
        builder.HasIndex(g => g.OrderId).IsUnique();
        builder.Property(g => g.OrderNumber).HasMaxLength(32).IsRequired();
        builder.Property(g => g.ProductCode).HasMaxLength(32).IsRequired();
        builder.Property(g => g.ReviewerId).HasMaxLength(32).IsRequired();
        builder.Property(g => g.TraceabilityLabelCode).HasMaxLength(64).IsRequired();
        builder.HasIndex(g => g.TraceabilityLabelCode).IsUnique();
        builder.Property(g => g.ReceivedAt).HasColumnType("timestamptz");
        builder.Property(g => g.SapSyncedAt).HasColumnType("timestamptz");
    }
}
