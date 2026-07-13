using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.MaterialAndSap;

/// <summary>SAP 拒单回写记录实体配置。</summary>
public sealed class SapRejectionRecordConfiguration : IEntityTypeConfiguration<SapRejectionRecord>
{
    public void Configure(EntityTypeBuilder<SapRejectionRecord> builder)
    {
        builder.ToTable("sap_rejection_records");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ExternalOrderNumber).HasMaxLength(64);
        builder.HasIndex(r => r.ExternalOrderNumber).HasDatabaseName("idx_sap_rejection_external");
        builder.Property(r => r.ProductCode).HasMaxLength(32);
        builder.Property(r => r.BomVersion).HasMaxLength(32);
        builder.Property(r => r.RoutingId).HasMaxLength(64);
        builder.Property(r => r.RejectionReason).HasMaxLength(512).IsRequired();
        builder.Property(r => r.WritebackStatus).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(r => r.WritebackStatus).HasDatabaseName("idx_sap_rejection_writeback");
        builder.Property(r => r.WritebackError).HasMaxLength(512);
        builder.Property(r => r.RejectedAt).HasColumnType("timestamptz");
        builder.Property(r => r.WritebackAt).HasColumnType("timestamptz");
    }
}
