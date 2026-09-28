using Dhole.Pricing.Domain.MarketPricing.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.MarketPricing;

internal sealed class AutoPricingProfileConfiguration
    : IEntityTypeConfiguration<AutoPricingProfile>
{
    public void Configure(EntityTypeBuilder<AutoPricingProfile> builder)
    {
        builder.ToTable("AutoPricingProfiles");

        builder.HasKey(x => x.Id).HasName("PK_AutoPricingProfiles");
        builder.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");

        builder.Property(x => x.Name).HasMaxLength(160).HasColumnName("name").IsRequired();
        builder.Property(x => x.Code).HasMaxLength(80).HasColumnName("code").IsRequired();

        builder.Property(x => x.TargetPercentile).HasPrecision(9, 4).HasColumnName("target_percentile");
        builder.Property(x => x.CompetitiveCeilingPercentile).HasPrecision(9, 4).HasColumnName("competitive_ceiling_percentile");

        builder.Property(x => x.MinimumConfidenceForAutoApply).HasPrecision(9, 4).HasColumnName("minimum_confidence_for_auto_apply");
        builder.Property(x => x.MinimumConfidenceForSuggestion).HasPrecision(9, 4).HasColumnName("minimum_confidence_for_suggestion");

        builder.Property(x => x.MinimumCompetitorCount).HasColumnName("minimum_competitor_count");
        builder.Property(x => x.MinimumObservationCount).HasColumnName("minimum_observation_count");

        builder.Property(x => x.MaximumMarketDeviation).HasPrecision(9, 4).HasColumnName("maximum_market_deviation");

        builder.Property(x => x.IsActive).HasColumnName("is_active");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone").HasColumnName("created_at_utc");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone").HasColumnName("updated_at_utc");

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasDatabaseName("UX_AutoPricingProfiles_Code");

        builder.HasIndex(x => x.IsActive)
            .HasDatabaseName("IX_AutoPricingProfiles_IsActive");
    }
}
