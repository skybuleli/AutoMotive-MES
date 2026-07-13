using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Equipment;

/// <summary>预防性维护计划实体配置。</summary>
public sealed class MaintenancePlanConfiguration : IEntityTypeConfiguration<MaintenancePlan>
{
    public void Configure(EntityTypeBuilder<MaintenancePlan> builder)
    {
        builder.ToTable("maintenance_plans");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.EquipmentCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(p => p.EquipmentCode).HasDatabaseName("idx_mt_plan_equipment");
        builder.Property(p => p.EquipmentName).HasMaxLength(64).IsRequired();
        builder.Property(p => p.MaintenanceType).HasConversion<int>();
        builder.Property(p => p.TaskDescription).HasMaxLength(128).IsRequired();
        builder.Property(p => p.WorkContent).HasMaxLength(1024);
        builder.Property(p => p.LastTriggeredAt).HasColumnType("timestamptz");
        builder.Property(p => p.CreatedAt).HasColumnType("timestamptz");
    }
}
