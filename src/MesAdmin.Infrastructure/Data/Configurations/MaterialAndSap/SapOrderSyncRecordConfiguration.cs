using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.MaterialAndSap;

/// <summary>SAP 工单状态同步记录实体配置。</summary>
public sealed class SapOrderSyncRecordConfiguration : IEntityTypeConfiguration<SapOrderSyncRecord>
{
    public void Configure(EntityTypeBuilder<SapOrderSyncRecord> builder)
    {
        builder.ToTable("sap_order_sync_records");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.OrderId).HasDatabaseName("idx_sap_order_sync_order");
        builder.Property(r => r.OrderNumber).HasMaxLength(32).IsRequired();
        builder.Property(r => r.ExternalOrderNumber).HasMaxLength(64).IsRequired();
        builder.HasIndex(r => r.ExternalOrderNumber).HasDatabaseName("idx_sap_order_sync_external");
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.SapDocumentNumber).HasMaxLength(64);
        builder.Property(r => r.SyncError).HasMaxLength(512);
        builder.HasIndex(r => r.SapSynced).HasDatabaseName("idx_sap_order_sync_status");
        builder.Property(r => r.SyncedAt).HasColumnType("timestamptz");
        builder.Property(r => r.CreatedAt).HasColumnType("timestamptz");
    }
}
