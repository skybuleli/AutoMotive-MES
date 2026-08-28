using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.System;

/// <summary>校准记录实体配置（S01）。</summary>
public sealed class CalibrationRecordConfiguration : IEntityTypeConfiguration<CalibrationRecord>
{
    public void Configure(EntityTypeBuilder<CalibrationRecord> builder)
    {
        builder.ToTable("calibration_records");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.GaugeId).IsRequired();
        builder.HasIndex(r => r.GaugeId).HasDatabaseName("idx_cal_records_gauge");
        builder.Property(r => r.Result).HasConversion<int>();
        builder.Property(r => r.CertificateNo).HasMaxLength(64).IsRequired();
        builder.Property(r => r.OperatorId).HasMaxLength(32).IsRequired();
        builder.Property(r => r.Remarks).HasMaxLength(256);
        builder.Property(r => r.CalibratedAt).HasColumnType("timestamptz");
        builder.Property(r => r.NextDueAfter).HasColumnType("timestamptz");
        builder.Property(r => r.CreatedAt).HasColumnType("timestamptz");
    }
}
