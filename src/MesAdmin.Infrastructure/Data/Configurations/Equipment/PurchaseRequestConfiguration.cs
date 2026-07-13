using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Equipment;

/// <summary>备件采购申请实体配置。</summary>
public sealed class PurchaseRequestConfiguration : IEntityTypeConfiguration<PurchaseRequest>
{
    public void Configure(EntityTypeBuilder<PurchaseRequest> builder)
    {
        builder.ToTable("purchase_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.RequestNumber).HasMaxLength(32).IsRequired();
        builder.HasIndex(r => r.RequestNumber).IsUnique();
        builder.HasIndex(r => r.SparePartId).HasDatabaseName("idx_purchase_request_part");
        builder.Property(r => r.Reason).HasMaxLength(256).IsRequired();
        builder.Property(r => r.Status).HasMaxLength(16).IsRequired();
        builder.HasIndex(r => r.Status).HasDatabaseName("idx_purchase_request_status");
        builder.Property(r => r.RequestedBy).HasMaxLength(32).IsRequired();
        builder.Property(r => r.ApprovedBy).HasMaxLength(32);
        builder.Property(r => r.ApprovedAt).HasColumnType("timestamptz");
        builder.Property(r => r.CreatedAt).HasColumnType("timestamptz");
        builder.Property(r => r.UpdatedAt).HasColumnType("timestamptz");
    }
}
