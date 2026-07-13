using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.MaterialAndSap;

/// <summary>物料消耗实体配置。</summary>
public sealed class MaterialConsumptionConfiguration : IEntityTypeConfiguration<MaterialConsumption>
{
    public void Configure(EntityTypeBuilder<MaterialConsumption> builder)
    {
        builder.ToTable("material_consumptions");
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => c.OrderId).HasDatabaseName("idx_consumption_order");
        builder.HasIndex(c => new { c.OrderId, c.MaterialCode }).IsUnique().HasDatabaseName("ux_consumption_order_material");
        builder.Property(c => c.OrderNumber).HasMaxLength(32).IsRequired();
        builder.Property(c => c.MaterialCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(c => c.MaterialCode).HasDatabaseName("idx_consumption_material");
        builder.Property(c => c.MaterialName).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Unit).HasMaxLength(16).IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnType("timestamptz");
    }
}
