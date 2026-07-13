using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Production;

/// <summary>JIT 拉动信号实体配置。</summary>
public sealed class JitPullSignalConfiguration : IEntityTypeConfiguration<JitPullSignal>
{
    public void Configure(EntityTypeBuilder<JitPullSignal> builder)
    {
        builder.ToTable("jit_pull_signals");
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.OrderId).HasDatabaseName("idx_jit_pull_order");
        builder.Property(s => s.OrderNumber).HasMaxLength(32).IsRequired();
        builder.Property(s => s.MaterialCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(s => s.MaterialCode).HasDatabaseName("idx_jit_pull_material");
        builder.Property(s => s.MaterialName).HasMaxLength(64).IsRequired();
        builder.Property(s => s.Unit).HasMaxLength(16).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(s => s.Status).HasDatabaseName("idx_jit_pull_status");
        builder.Property(s => s.TargetStation).HasMaxLength(32);
        builder.Property(s => s.DeliveredBy).HasMaxLength(32);
        builder.Property(s => s.Remarks).HasMaxLength(256);
        builder.Property(s => s.CreatedAt).HasColumnType("timestamptz");
        builder.Property(s => s.DeliveredAt).HasColumnType("timestamptz");
    }
}
