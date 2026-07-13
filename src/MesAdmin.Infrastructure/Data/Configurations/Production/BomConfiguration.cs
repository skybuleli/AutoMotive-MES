using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Production;

/// <summary>BOM 实体配置。</summary>
public sealed class BomConfiguration : IEntityTypeConfiguration<Bom>
{
    public void Configure(EntityTypeBuilder<Bom> builder)
    {
        builder.ToTable("boms");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProductCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Version).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.ProductCode, x.Version }).IsUnique();
        builder.Property(x => x.EffectiveDate).HasColumnType("timestamptz");
        builder.Property(x => x.ExpirationDate).HasColumnType("timestamptz");
        builder.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        builder.OwnsMany(x => x.Items, p =>
        {
            p.ToJson();
        });
    }
}
