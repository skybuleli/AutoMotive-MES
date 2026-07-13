using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Production;

/// <summary>追溯链实体配置。</summary>
public sealed class TraceabilityLinkConfiguration : IEntityTypeConfiguration<TraceabilityLink>
{
    public void Configure(EntityTypeBuilder<TraceabilityLink> builder)
    {
        builder.ToTable("traceability_links");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.VinOrSerial).HasMaxLength(64);
        builder.HasIndex(l => l.VinOrSerial).HasDatabaseName("idx_trace_vin");
        builder.Property(l => l.ComponentBatch).HasMaxLength(64);
        builder.HasIndex(l => l.ComponentBatch).HasDatabaseName("idx_trace_component");
        builder.Property(l => l.MaterialBatch).HasMaxLength(64);
        builder.HasIndex(l => l.MaterialBatch).HasDatabaseName("idx_trace_material");
        builder.Property(l => l.CreatedAt).HasColumnType("timestamptz");
        builder.HasIndex(l => new { l.VinOrSerial, l.Level }).IsUnique();
    }
}
