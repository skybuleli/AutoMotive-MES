using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Production;

/// <summary>工艺路线实体配置。</summary>
public sealed class RoutingConfiguration : IEntityTypeConfiguration<Routing>
{
    public void Configure(EntityTypeBuilder<Routing> builder)
    {
        builder.ToTable("routings");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ProductCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(r => r.ProductCode).HasDatabaseName("idx_routing_product");
        builder.Property(r => r.Name).HasMaxLength(128).IsRequired();
        builder.Property(r => r.Version).HasMaxLength(16).IsRequired();
        builder.HasIndex(r => new { r.ProductCode, r.Version }).IsUnique().HasDatabaseName("ux_routing_product_version");
        builder.Property(r => r.EcoNumber).HasMaxLength(32);
        builder.Property(r => r.EcoStatus).HasConversion<int>();
        builder.HasIndex(r => r.EcoStatus).HasDatabaseName("idx_routing_eco_status");
        builder.Property(r => r.IsActive).HasDefaultValue(false);
        builder.HasIndex(r => r.IsActive).HasDatabaseName("idx_routing_active");
        builder.Property(r => r.ChangeDescription).HasMaxLength(1024);
        builder.Property(r => r.CreatedBy).HasMaxLength(32).IsRequired();
        builder.Property(r => r.ApprovedBy).HasMaxLength(32);
        builder.Property(r => r.ApprovedAt).HasColumnType("timestamptz");
        builder.Property(r => r.CreatedAt).HasColumnType("timestamptz");
        builder.Property(r => r.UpdatedAt).HasColumnType("timestamptz");
        builder.Property(r => r.EffectiveDate).HasColumnType("timestamptz");
        builder.Property(r => r.ExpirationDate).HasColumnType("timestamptz");

        builder.OwnsMany(r => r.Operations, op =>
        {
            op.ToJson();
            op.Property(x => x.OperationCode).HasMaxLength(32).IsRequired();
            op.Property(x => x.OperationName).HasMaxLength(64).IsRequired();
            op.Property(x => x.FixtureCode).HasMaxLength(32);
            op.Property(x => x.FixtureName).HasMaxLength(64);

            op.OwnsMany(x => x.ParameterTemplates, pt =>
            {
                pt.Property(p => p.ParameterCode).HasMaxLength(32).IsRequired();
                pt.Property(p => p.ParameterName).HasMaxLength(64).IsRequired();
                pt.Property(p => p.Unit).HasMaxLength(16).IsRequired();
            });
        });
    }
}
