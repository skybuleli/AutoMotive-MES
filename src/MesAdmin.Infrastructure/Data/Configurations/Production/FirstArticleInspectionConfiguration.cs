using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Production;

/// <summary>首件检验实体配置。</summary>
public sealed class FirstArticleInspectionConfiguration : IEntityTypeConfiguration<FirstArticleInspection>
{
    public void Configure(EntityTypeBuilder<FirstArticleInspection> builder)
    {
        builder.ToTable("first_article_inspections");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.OrderNumber).HasMaxLength(32).IsRequired();
        builder.Property(f => f.ProductCode).HasMaxLength(32).IsRequired();
        builder.Property(f => f.InspectionType).HasMaxLength(32).IsRequired();
        builder.Property(f => f.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(f => f.OperatorId).HasMaxLength(32);
        builder.Property(f => f.GaugeId).IsRequired(false);
        builder.HasIndex(f => f.GaugeId).HasDatabaseName("idx_fai_gauge");
        builder.Property(f => f.InspectorId).HasMaxLength(32);
        builder.Property(f => f.Conclusion).HasMaxLength(256);
        builder.Property(f => f.CreatedAt).HasColumnType("timestamptz");
        builder.Property(f => f.CompletedAt).HasColumnType("timestamptz");
        builder.OwnsMany(f => f.Items, p =>
        {
            p.ToJson();
        });
    }
}
