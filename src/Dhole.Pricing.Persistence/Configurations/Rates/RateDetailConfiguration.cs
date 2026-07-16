using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.Pricing.Domain.Rates.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.Rates;

internal sealed class RateDetailConfiguration : EntityTypeConfigurationBase<RateDetail, Guid>
{
    public override void Configure(EntityTypeBuilder<RateDetail> builder)
    {
        base.Configure(builder);

        builder.ToTable("RateDetails");

        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.RateHeaderId).IsRequired();

        builder.Property(x => x.CostId).IsRequired(false);

        builder.Property(x => x.Name).HasMaxLength(250).IsRequired();

        builder
            .Property(x => x.CostDetailType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CostType).HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.Property(x => x.CurrencyId).IsRequired();

        builder.Property(x => x.CurrencyName).HasMaxLength(120).IsRequired();

        builder.Property(x => x.CurrencyCode).HasMaxLength(20).IsRequired();

        builder.Property(x => x.CostAmount).HasPrecision(18, 2).IsRequired();

        builder.Property(x => x.SaleAmount).HasPrecision(18, 2).IsRequired();

        builder.Property(x => x.UtilityAmount).HasPrecision(18, 2).IsRequired();

        builder.Property(x => x.Notes).HasColumnType("text").IsRequired(false);

        builder.Property(x => x.Quantity).IsRequired().HasDefaultValue(1);

        builder.HasIndex(x => x.RateHeaderId);
        builder.HasIndex(x => x.CostId);
        builder.HasIndex(x => x.CostDetailType);
        builder.HasIndex(x => x.CostType);
        builder.HasIndex(x => x.CurrencyId);

        builder
            .HasIndex(x => new
            {
                x.RateHeaderId,
                x.CostId,
                x.Name,
                x.CostDetailType,
                x.CostType,
                x.CurrencyId,
            })
            .HasDatabaseName("ix_rate_details_lookup");
    }
}
