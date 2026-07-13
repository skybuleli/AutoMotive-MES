using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Quality;

/// <summary>检验计划实体配置。</summary>
public sealed class InspectionPlanConfiguration : IEntityTypeConfiguration<InspectionPlan>
{
    public void Configure(EntityTypeBuilder<InspectionPlan> builder)
    {
        builder.ToTable("inspection_plans");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.PlanName).HasMaxLength(64).IsRequired();
        builder.Property(p => p.Version).HasMaxLength(16).IsRequired();
        builder.HasIndex(p => new { p.PlanName, p.Version }).IsUnique();
        builder.Property(p => p.ProductCode).HasMaxLength(32);
        builder.HasIndex(p => p.ProductCode).HasDatabaseName("idx_inspection_plan_product");
        builder.Property(p => p.Stage).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.SamplingFrequency).HasMaxLength(32).IsRequired();
        builder.Property(p => p.InspectionLevel).HasMaxLength(8);
        builder.Property(p => p.EffectiveDate).HasColumnType("timestamptz");
        builder.Property(p => p.ExpirationDate).HasColumnType("timestamptz");
        builder.Property(p => p.CreatedAt).HasColumnType("timestamptz");

        builder.OwnsMany(p => p.Characteristics, c =>
        {
            c.ToJson();
            c.Property(x => x.CharacteristicCode).HasMaxLength(32).IsRequired();
            c.Property(x => x.CharacteristicName).HasMaxLength(64).IsRequired();
            c.Property(x => x.Unit).HasMaxLength(16).IsRequired();
            c.Property(x => x.MeasurementTool).HasMaxLength(32);
        });
    }
}
