using Dhole.Pricing.Domain.Costs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.Costs;

internal sealed class CostIncotermConfiguration : IEntityTypeConfiguration<CostIncoterm>
{
    public void Configure(EntityTypeBuilder<CostIncoterm> builder)
    {
        builder.ToTable("CostIncoterms");
        builder.HasKey(x => new { x.CostId, x.IncotermId });
        builder.Property(x => x.CostId).IsRequired();
        builder.Property(x => x.IncotermId).IsRequired();
        builder.Property(x => x.IncotermName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.IncotermCode).HasMaxLength(40).IsRequired();
        builder.HasIndex(x => x.IncotermId);
        builder.HasIndex(x => x.IncotermCode);
    }
}
