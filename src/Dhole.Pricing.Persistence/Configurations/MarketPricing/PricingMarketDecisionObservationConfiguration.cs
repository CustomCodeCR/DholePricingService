using Dhole.Pricing.Domain.MarketPricing.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.MarketPricing;

internal sealed class PricingMarketDecisionObservationConfiguration
    : IEntityTypeConfiguration<PricingMarketDecisionObservation>
{
    public void Configure(EntityTypeBuilder<PricingMarketDecisionObservation> builder)
    {
        builder.ToTable("PricingMarketDecisionObservations");

        builder.HasKey(x => x.Id).HasName("PK_PricingMarketDecisionObservations");
        builder.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");

        builder.Property(x => x.PricingMarketDecisionId).HasColumnName("pricing_market_decision_id").IsRequired();
        builder.Property(x => x.CompetitorRateObservationId).HasColumnName("competitor_rate_observation_id").IsRequired();
        builder.Property(x => x.CompetitorCompanyId).HasColumnName("competitor_company_id");

        builder.Property(x => x.OriginalAmount).HasPrecision(18, 2).HasColumnName("original_amount").IsRequired(false);
        builder.Property(x => x.NormalizedAmount).HasPrecision(18, 2).HasColumnName("normalized_amount").IsRequired(false);

        builder.Property(x => x.ComparabilityScore).HasPrecision(9, 6).HasColumnName("comparability_score");
        builder.Property(x => x.RecencyWeight).HasPrecision(9, 6).HasColumnName("recency_weight");
        builder.Property(x => x.FinalWeight).HasPrecision(9, 6).HasColumnName("final_weight");

        builder.Property(x => x.IsOutlier).HasColumnName("is_outlier");
        builder.Property(x => x.WasIncluded).HasColumnName("was_included");

        builder.Property(x => x.ExclusionReason).HasMaxLength(500).HasColumnName("exclusion_reason");

        builder.HasOne<PricingMarketDecision>()
            .WithMany()
            .HasForeignKey(x => x.PricingMarketDecisionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_PricingMarketDecisionObservations_Decisions");

        builder.HasOne<CompetitorRateObservation>()
            .WithMany()
            .HasForeignKey(x => x.CompetitorRateObservationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_PricingMarketDecisionObservations_Observations");

        builder.HasIndex(x => new { x.PricingMarketDecisionId, x.CompetitorRateObservationId })
            .IsUnique()
            .HasDatabaseName("UX_PricingMarketDecisionObservations_Decision_Observation");

        builder.HasIndex(x => x.CompetitorRateObservationId)
            .HasDatabaseName("IX_PricingMarketDecisionObservations_ObservationId");
    }
}
