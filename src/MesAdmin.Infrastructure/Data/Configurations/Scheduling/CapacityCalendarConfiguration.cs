using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Scheduling;

/// <summary>产能日历实体配置。</summary>
public sealed class CapacityCalendarConfiguration : IEntityTypeConfiguration<CapacityCalendar>
{
    public void Configure(EntityTypeBuilder<CapacityCalendar> builder)
    {
        builder.ToTable("capacity_calendars");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.EquipmentCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(c => c.EquipmentCode).IsUnique();
        builder.Property(c => c.EquipmentName).HasMaxLength(64).IsRequired();
        builder.Property(c => c.ShiftTemplate).HasMaxLength(256).IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnType("timestamptz");
        builder.Property(c => c.UpdatedAt).HasColumnType("timestamptz");
    }
}
