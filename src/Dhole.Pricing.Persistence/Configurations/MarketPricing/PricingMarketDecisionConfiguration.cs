using Dhole.Pricing.Domain.MarketPricing.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.MarketPricing;

internal sealed class PricingMarketDecisionConfiguration
    : IEntityTypeConfiguration<PricingMarketDecision>
{
    public void Configure(EntityTypeBuilder<PricingMarketDecision> builder)
    {
        builder.ToTable("PricingMarketDecisions");

        builder.HasKey(x => x.Id).HasName("PK_PricingMarketDecisions");
        builder.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");

        builder.Property(x => x.RateId).HasColumnName("rate_id").IsRequired();
        builder.Property(x => x.CalculatedAtUtc).HasColumnType("timestamp with time zone").HasColumnName("calculated_at_utc");

        builder.Property(x => x.ComparisonIncotermId).HasColumnName("comparison_incoterm_id");
        builder.Property(x => x.ComparisonPolId).HasColumnName("comparison_pol_id").IsRequired();
        builder.Property(x => x.ComparisonPoeId).HasColumnName("comparison_poe_id");
        builder.Property(x => x.ComparisonPodId).HasColumnName("comparison_pod_id");
        builder.Property(x => x.ComparisonContainerTypeId).HasColumnName("comparison_container_type_id");
        builder.Property(x => x.ComparisonMode)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasColumnName("comparison_mode")
            .IsRequired();
        builder.Property(x => x.ComparisonCarrierId).HasColumnName("comparison_carrier_id");

        builder.Property(x => x.CostTotal).HasPrecision(18, 2).HasColumnName("cost_total");
        builder.Property(x => x.OriginalSaleTotal).HasPrecision(18, 2).HasColumnName("original_sale_total");
        builder.Property(x => x.SuggestedSaleTotal).HasPrecision(18, 2).HasColumnName("suggested_sale_total");
        builder.Property(x => x.FinalSaleTotal).HasPrecision(18, 2).HasColumnName("final_sale_total");

        builder.Property(x => x.Average).HasPrecision(18, 2).HasColumnName("average");
        builder.Property(x => x.WeightedAverage).HasPrecision(18, 2).HasColumnName("weighted_average");
        builder.Property(x => x.Median).HasPrecision(18, 2).HasColumnName("median");
        builder.Property(x => x.P25).HasPrecision(18, 2).HasColumnName("p25");
        builder.Property(x => x.P40).HasPrecision(18, 2).HasColumnName("p40");
        builder.Property(x => x.P50).HasPrecision(18, 2).HasColumnName("p50");
        builder.Property(x => x.P60).HasPrecision(18, 2).HasColumnName("p60");
        builder.Property(x => x.P65).HasPrecision(18, 2).HasColumnName("p65");
        builder.Property(x => x.P75).HasPrecision(18, 2).HasColumnName("p75");

        builder.Property(x => x.TargetMarketPrice).HasPrecision(18, 2).HasColumnName("target_market_price");
        builder.Property(x => x.CompetitiveCeiling).HasPrecision(18, 2).HasColumnName("competitive_ceiling");

        builder.Property(x => x.CompetitorCount).HasColumnName("competitor_count");
        builder.Property(x => x.ObservationCount).HasColumnName("observation_count");
        builder.Property(x => x.ConfidenceScore).HasPrecision(9, 4).HasColumnName("confidence_score");

        builder.Property(x => x.AlgorithmVersion).HasMaxLength(80).HasColumnName("algorithm_version").IsRequired();
        builder.Property(x => x.WasAutoApplied).HasColumnName("was_auto_applied");
        builder.Property(x => x.WasManuallyModified).HasColumnName("was_manually_modified");

        builder.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
        builder.Property(x => x.ReviewedAtUtc).HasColumnType("timestamp with time zone").HasColumnName("reviewed_at_utc");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone").HasColumnName("created_at_utc");

        builder.HasIndex(x => new { x.RateId, x.CalculatedAtUtc })
            .HasDatabaseName("IX_PricingMarketDecisions_Rate_CalculatedAt");

        builder.HasIndex(x => new
            {
                x.ComparisonMode,
                x.ComparisonIncotermId,
                x.ComparisonPolId,
                x.ComparisonPoeId,
                x.ComparisonPodId,
                x.ComparisonContainerTypeId,
                x.ComparisonCarrierId,
            })
            .HasDatabaseName("IX_PricingMarketDecisions_MarketKey");
    }
}
