using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Equipment;

/// <summary>维护工单实体配置。</summary>
public sealed class MaintenanceWorkOrderConfiguration : IEntityTypeConfiguration<MaintenanceWorkOrder>
{
    public void Configure(EntityTypeBuilder<MaintenanceWorkOrder> builder)
    {
        builder.ToTable("maintenance_work_orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.OrderNumber).HasMaxLength(32).IsRequired();
        builder.HasIndex(o => o.OrderNumber).IsUnique();
        builder.HasIndex(o => o.MaintenancePlanId).HasDatabaseName("idx_mt_order_plan");
        builder.Property(o => o.EquipmentCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(o => o.EquipmentCode).HasDatabaseName("idx_mt_order_equipment");
        builder.Property(o => o.EquipmentName).HasMaxLength(64).IsRequired();
        builder.Property(o => o.MaintenanceType).HasConversion<int>();
        builder.Property(o => o.TriggerType).HasConversion<int>();
        builder.Property(o => o.Title).HasMaxLength(128).IsRequired();
        builder.Property(o => o.Description).HasMaxLength(1024);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(o => o.Status).HasDatabaseName("idx_mt_order_status");
        builder.Property(o => o.AssignedTo).HasMaxLength(32);
        builder.Property(o => o.CompletedBy).HasMaxLength(32);
        builder.Property(o => o.CompletionRemarks).HasMaxLength(512);
        builder.Property(o => o.CompletedAt).HasColumnType("timestamptz");
        builder.Property(o => o.CreatedAt).HasColumnType("timestamptz");
        builder.HasIndex(o => o.CreatedAt).HasDatabaseName("idx_mt_order_created_at");
    }
}
