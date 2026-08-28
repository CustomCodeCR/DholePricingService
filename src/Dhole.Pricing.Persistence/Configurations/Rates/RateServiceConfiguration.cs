using Dhole.Pricing.Domain.Rates.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.Rates;

internal sealed class RateServiceConfiguration : IEntityTypeConfiguration<RateService>
{
    public void Configure(EntityTypeBuilder<RateService> builder)
    {
        builder.ToTable("RateServices");
        builder.HasKey(x => new { x.RateHeaderId, x.ServiceId });
        builder.Property(x => x.ServiceName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.ServiceCode).HasMaxLength(80).IsRequired();
        builder.HasIndex(x => x.ServiceId);
        builder.HasIndex(x => x.ServiceCode);
    }
}
