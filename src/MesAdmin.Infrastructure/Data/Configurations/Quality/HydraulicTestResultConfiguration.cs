using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Quality;

/// <summary>100% 在线液压测试结果实体配置。</summary>
public sealed class HydraulicTestResultConfiguration : IEntityTypeConfiguration<HydraulicTestResult>
{
    public void Configure(EntityTypeBuilder<HydraulicTestResult> builder)
    {
        builder.ToTable("hydraulic_test_results");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.EquipmentCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(r => r.EquipmentCode).HasDatabaseName("idx_hydraulic_equipment");
        builder.HasIndex(r => r.OrderId).HasDatabaseName("idx_hydraulic_order");
        builder.Property(r => r.ProductSerial).HasMaxLength(64);
        builder.HasIndex(r => r.ProductSerial).HasDatabaseName("idx_hydraulic_serial");
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(r => r.Status).HasDatabaseName("idx_hydraulic_status");
        builder.Property(r => r.FailureReason).HasMaxLength(512);
        builder.Property(r => r.UnlockedBy).HasMaxLength(32);
        builder.Property(r => r.UnlockedAt).HasColumnType("timestamptz");
        builder.Property(r => r.StartedAt).HasColumnType("timestamptz");
        builder.Property(r => r.CompletedAt).HasColumnType("timestamptz");
        builder.Property(r => r.CreatedAt).HasColumnType("timestamptz");

        builder.OwnsMany(r => r.SolenoidTests, s =>
        {
            s.ToJson();
            s.Property(x => x.FaultCode).HasMaxLength(16);
        });
    }
}
