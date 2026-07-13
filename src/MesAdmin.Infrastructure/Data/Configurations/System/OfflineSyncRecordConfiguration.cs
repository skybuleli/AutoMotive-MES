using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.System;

/// <summary>终端离线同步记录实体配置。</summary>
public sealed class OfflineSyncRecordConfiguration : IEntityTypeConfiguration<OfflineSyncRecord>
{
    public void Configure(EntityTypeBuilder<OfflineSyncRecord> builder)
    {
        builder.ToTable("offline_sync_records");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.TerminalId).HasMaxLength(32).IsRequired();
        builder.HasIndex(r => r.TerminalId).HasDatabaseName("idx_offline_terminal");
        builder.Property(r => r.OperationType).HasMaxLength(32).IsRequired();
        builder.HasIndex(r => r.OperationType).HasDatabaseName("idx_offline_op_type");
        builder.Property(r => r.EntityType).HasMaxLength(32).IsRequired();
        builder.HasIndex(r => r.EntityType).HasDatabaseName("idx_offline_entity_type");
        builder.Property(r => r.EntityId).HasMaxLength(64);
        builder.HasIndex(r => r.EntityId).HasDatabaseName("idx_offline_entity_id");
        builder.Property(r => r.Status).HasMaxLength(16).IsRequired();
        builder.HasIndex(r => r.Status).HasDatabaseName("idx_offline_status");
        builder.Property(r => r.ErrorMessage).HasMaxLength(512);
        builder.Property(r => r.ConflictResolution).HasMaxLength(16);
        builder.Property(r => r.OperationTimestamp).HasColumnType("timestamptz");
        builder.Property(r => r.CreatedAt).HasColumnType("timestamptz");
        builder.HasIndex(r => r.CreatedAt).HasDatabaseName("idx_offline_created_at");
        builder.Property(r => r.LastAttemptAt).HasColumnType("timestamptz");
        builder.Property(r => r.SyncedAt).HasColumnType("timestamptz");
    }
}
