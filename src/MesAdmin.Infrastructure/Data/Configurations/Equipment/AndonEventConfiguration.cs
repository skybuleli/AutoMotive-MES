using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Equipment;

/// <summary>安灯事件实体配置。</summary>
public sealed class AndonEventConfiguration : IEntityTypeConfiguration<AndonEvent>
{
    public void Configure(EntityTypeBuilder<AndonEvent> builder)
    {
        builder.ToTable("andon_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EventNumber).HasMaxLength(32).IsRequired();
        builder.HasIndex(e => e.EventNumber).IsUnique();
        builder.Property(e => e.EquipmentCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(e => e.EquipmentCode).HasDatabaseName("idx_andon_equipment");
        builder.Property(e => e.AlarmType).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.Severity).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(e => e.Status).HasDatabaseName("idx_andon_status");
        builder.Property(e => e.Description).HasMaxLength(256).IsRequired();
        builder.Property(e => e.ProcessTag).HasMaxLength(32);
        builder.Property(e => e.AcknowledgedBy).HasMaxLength(32);
        builder.Property(e => e.Resolution).HasMaxLength(512);
        builder.Property(e => e.ResolvedBy).HasMaxLength(32);
        builder.Property(e => e.CloseRemarks).HasMaxLength(512);
        builder.Property(e => e.OccurredAt).HasColumnType("timestamptz");
        builder.HasIndex(e => e.OccurredAt).HasDatabaseName("idx_andon_occurred");
        builder.Property(e => e.EscalatedAt).HasColumnType("timestamptz");
        builder.Property(e => e.AcknowledgedAt).HasColumnType("timestamptz");
        builder.Property(e => e.ResolvedAt).HasColumnType("timestamptz");
        builder.Property(e => e.ClosedAt).HasColumnType("timestamptz");
        builder.Property(e => e.CreatedAt).HasColumnType("timestamptz");
        builder.HasIndex(e => e.CreatedAt).HasDatabaseName("idx_andon_created_at");
    }
}
