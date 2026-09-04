using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.Pricing.Domain.Rates.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.Rates;

internal sealed class RateComparisonDetailConfiguration : EntityTypeConfigurationBase<RateComparisonDetail, Guid>
{
    public override void Configure(EntityTypeBuilder<RateComparisonDetail> builder)
    {
        base.Configure(builder);
        builder.ToTable("RateComparisonDetails");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(250).IsRequired();
        builder.Property(x => x.CostDetailType).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.CostType).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.ChargeBasis).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(18, 6).IsRequired();
        builder.Property(x => x.BaselineCostAmount).HasPrecision(18, 2);
        builder.Property(x => x.BaselineSaleAmount).HasPrecision(18, 2);
        builder.Property(x => x.CandidateCostAmount).HasPrecision(18, 2);
        builder.Property(x => x.CandidateSaleAmount).HasPrecision(18, 2);
        builder.Property(x => x.Notes).HasColumnType("text").IsRequired(false);
        builder.HasIndex(x => x.RateComparisonId);
    }
}
