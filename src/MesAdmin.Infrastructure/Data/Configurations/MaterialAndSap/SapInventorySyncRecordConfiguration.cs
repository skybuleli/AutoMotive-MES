using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.MaterialAndSap;

/// <summary>SAP 库存同步记录实体配置。</summary>
public sealed class SapInventorySyncRecordConfiguration : IEntityTypeConfiguration<SapInventorySyncRecord>
{
    public void Configure(EntityTypeBuilder<SapInventorySyncRecord> builder)
    {
        builder.ToTable("sap_inventory_sync_records");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.OrderId).HasDatabaseName("idx_sap_inv_sync_order");
        builder.Property(r => r.OrderNumber).HasMaxLength(32).IsRequired();
        builder.Property(r => r.MaterialCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(r => r.MaterialCode).HasDatabaseName("idx_sap_inv_sync_material");
        builder.Property(r => r.MovementType).HasMaxLength(8).IsRequired();
        builder.Property(r => r.Unit).HasMaxLength(16).IsRequired();
        builder.Property(r => r.SapDocumentNumber).HasMaxLength(64);
        builder.Property(r => r.SyncError).HasMaxLength(512);
        builder.HasIndex(r => r.SapSynced).HasDatabaseName("idx_sap_inv_sync_status");
        builder.Property(r => r.SyncedAt).HasColumnType("timestamptz");
        builder.Property(r => r.CreatedAt).HasColumnType("timestamptz");
    }
}
