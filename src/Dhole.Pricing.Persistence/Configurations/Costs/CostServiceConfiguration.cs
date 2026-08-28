using Dhole.Pricing.Domain.Costs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.Costs;

internal sealed class CostServiceConfiguration : IEntityTypeConfiguration<CostService>
{
    public void Configure(EntityTypeBuilder<CostService> builder)
    {
        builder.ToTable("CostServices");
        builder.HasKey(x => new { x.CostId, x.ServiceId });
        builder.Property(x => x.CostId).IsRequired();
        builder.Property(x => x.ServiceId).IsRequired();
        builder.Property(x => x.ServiceName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.ServiceCode).HasMaxLength(80).IsRequired();
        builder.HasIndex(x => x.ServiceId);
        builder.HasIndex(x => x.ServiceCode);
    }
}
