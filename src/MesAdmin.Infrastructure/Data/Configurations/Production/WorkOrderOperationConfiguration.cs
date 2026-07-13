using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Production;

/// <summary>工单工序实体配置。</summary>
public sealed class WorkOrderOperationConfiguration : IEntityTypeConfiguration<WorkOrderOperation>
{
    public void Configure(EntityTypeBuilder<WorkOrderOperation> builder)
    {
        builder.ToTable("work_order_operations");
        builder.HasKey(o => o.Id);
        builder.HasIndex(o => new { o.OrderId, o.Sequence }).IsUnique();
        builder.Property(o => o.OperationCode).HasMaxLength(32).IsRequired();
        builder.Property(o => o.OperationName).HasMaxLength(64).IsRequired();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(o => o.OperatorId).HasMaxLength(32);
        builder.Property(o => o.EquipmentId).HasMaxLength(32);
        builder.Property(o => o.FailureReason).HasMaxLength(256);
        builder.Property(o => o.Remarks).HasMaxLength(512);
        builder.Property(o => o.StartAt).HasColumnType("timestamptz");
        builder.Property(o => o.EndAt).HasColumnType("timestamptz");
        builder.Property(o => o.CreatedAt).HasColumnType("timestamptz");

        builder.OwnsMany(o => o.Parameters, p =>
        {
            p.ToJson();
        });
    }
}
