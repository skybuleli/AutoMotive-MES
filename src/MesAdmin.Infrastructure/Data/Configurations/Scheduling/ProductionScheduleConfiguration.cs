using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Scheduling;

/// <summary>生产排程实体配置。</summary>
public sealed class ProductionScheduleConfiguration : IEntityTypeConfiguration<ProductionSchedule>
{
    public void Configure(EntityTypeBuilder<ProductionSchedule> builder)
    {
        builder.ToTable("production_schedules");
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.OrderId).HasDatabaseName("idx_schedule_order");
        builder.Property(s => s.OrderNumber).HasMaxLength(32).IsRequired();
        builder.Property(s => s.ProductCode).HasMaxLength(32).IsRequired();
        builder.Property(s => s.EquipmentCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(s => s.EquipmentCode).HasDatabaseName("idx_schedule_equipment");
        builder.HasIndex(s => s.ScheduleDate).HasDatabaseName("idx_schedule_date");
        builder.Property(s => s.Status).HasConversion<int>();
        builder.HasIndex(s => s.Status).HasDatabaseName("idx_schedule_status");
        builder.Property(s => s.RushType).HasConversion<int>();
        builder.Property(s => s.RushReason).HasMaxLength(256);
        builder.Property(s => s.Remarks).HasMaxLength(512);
        builder.Property(s => s.PlannedStartAt).HasColumnType("timestamptz");
        builder.Property(s => s.PlannedEndAt).HasColumnType("timestamptz");
        builder.Property(s => s.CreatedAt).HasColumnType("timestamptz");
        builder.Property(s => s.UpdatedAt).HasColumnType("timestamptz");
    }
}
