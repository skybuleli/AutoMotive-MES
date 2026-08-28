using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.System;

/// <summary>计量器具台账实体配置（S01）。</summary>
public sealed class GaugeConfiguration : IEntityTypeConfiguration<Gauge>
{
    public void Configure(EntityTypeBuilder<Gauge> builder)
    {
        builder.ToTable("gauges");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.GaugeNumber).HasMaxLength(32).IsRequired();
        builder.HasIndex(g => g.GaugeNumber).IsUnique();
        builder.Property(g => g.Name).HasMaxLength(64).IsRequired();
        builder.Property(g => g.Type).HasConversion<int>();
        builder.Property(g => g.Status).HasConversion<int>();
        builder.Property(g => g.RangeSpec).HasMaxLength(64);
        builder.Property(g => g.ResolutionSpec).HasMaxLength(64);
        builder.Property(g => g.AccuracyClass).HasMaxLength(32);
        builder.Property(g => g.StorageLocation).HasMaxLength(128);
        builder.Property(g => g.Remarks).HasMaxLength(256);
        builder.Property(g => g.LastCalibratedAt).HasColumnType("timestamptz");
        builder.Property(g => g.NextDueAt).HasColumnType("timestamptz");
        builder.Property(g => g.CreatedAt).HasColumnType("timestamptz");
        builder.Property(g => g.UpdatedAt).HasColumnType("timestamptz");

        // 台账页默认按状态筛选 + 到期日排序，均为高频查询列
        builder.HasIndex(g => g.Status).HasDatabaseName("idx_gauges_status");
        builder.HasIndex(g => g.NextDueAt).HasDatabaseName("idx_gauges_next_due");
    }
}
