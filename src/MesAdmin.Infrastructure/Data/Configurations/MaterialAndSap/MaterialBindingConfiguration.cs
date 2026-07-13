using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.MaterialAndSap;

/// <summary>物料投料绑定实体配置。</summary>
public sealed class MaterialBindingConfiguration : IEntityTypeConfiguration<MaterialBinding>
{
    public void Configure(EntityTypeBuilder<MaterialBinding> builder)
    {
        builder.ToTable("material_bindings");
        builder.HasKey(b => b.Id);
        builder.HasIndex(b => b.OrderId).HasDatabaseName("idx_binding_order");
        builder.HasIndex(b => b.MaterialBatchId).HasDatabaseName("idx_binding_batch");
        builder.Property(b => b.MaterialCode).HasMaxLength(32).IsRequired();
        builder.Property(b => b.BatchNumber).HasMaxLength(64).IsRequired();
        builder.Property(b => b.ProductSerial).HasMaxLength(64).IsRequired();
        builder.HasIndex(b => b.ProductSerial).HasDatabaseName("idx_binding_serial");
        builder.Property(b => b.OperatorId).HasMaxLength(32).IsRequired();
        builder.Property(b => b.BoundAt).HasColumnType("timestamptz");
    }
}
