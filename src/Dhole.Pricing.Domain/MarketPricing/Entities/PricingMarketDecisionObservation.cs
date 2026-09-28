using CustomCodeFramework.Core.Domain.Entities;

namespace Dhole.Pricing.Domain.MarketPricing.Entities;

public sealed class PricingMarketDecisionObservation : Entity<Guid>
{
    private PricingMarketDecisionObservation() { }

    private PricingMarketDecisionObservation(
        Guid id,
        Guid pricingMarketDecisionId,
        Guid competitorRateObservationId,
        Guid? competitorCompanyId,
        decimal? originalAmount,
        decimal? normalizedAmount,
        decimal comparabilityScore,
        decimal recencyWeight,
        decimal finalWeight,
        bool isOutlier,
        bool wasIncluded,
        string? exclusionReason
    ) : base(id)
    {
        if (pricingMarketDecisionId == Guid.Empty || competitorRateObservationId == Guid.Empty)
        {
            throw new InvalidOperationException("La decisión y la observación de mercado son obligatorias.");
        }

        PricingMarketDecisionId = pricingMarketDecisionId;
        CompetitorRateObservationId = competitorRateObservationId;
        CompetitorCompanyId = competitorCompanyId;
        OriginalAmount = EnsureNonNegative(originalAmount, nameof(originalAmount));
        NormalizedAmount = EnsureNonNegative(normalizedAmount, nameof(normalizedAmount));
        ComparabilityScore = EnsurePercent(comparabilityScore, nameof(comparabilityScore));
        RecencyWeight = EnsureUnitInterval(recencyWeight, nameof(recencyWeight));
        FinalWeight = EnsureUnitInterval(finalWeight, nameof(finalWeight));
        IsOutlier = isOutlier;
        WasIncluded = wasIncluded;
        ExclusionReason = string.IsNullOrWhiteSpace(exclusionReason) ? null : exclusionReason.Trim();
    }

    public Guid PricingMarketDecisionId { get; private set; }
    public Guid CompetitorRateObservationId { get; private set; }
    public Guid? CompetitorCompanyId { get; private set; }

    public decimal? OriginalAmount { get; private set; }
    public decimal? NormalizedAmount { get; private set; }

    public decimal ComparabilityScore { get; private set; }
    public decimal RecencyWeight { get; private set; }
    public decimal FinalWeight { get; private set; }

    public bool IsOutlier { get; private set; }
    public bool WasIncluded { get; private set; }

    public string? ExclusionReason { get; private set; }

    public static PricingMarketDecisionObservation Create(
        Guid pricingMarketDecisionId,
        Guid competitorRateObservationId,
        Guid? competitorCompanyId,
        decimal? originalAmount,
        decimal? normalizedAmount,
        decimal comparabilityScore,
        decimal recencyWeight,
        decimal finalWeight,
        bool isOutlier,
        bool wasIncluded,
        string? exclusionReason
    ) => new(
        Guid.NewGuid(),
        pricingMarketDecisionId,
        competitorRateObservationId,
        competitorCompanyId,
        originalAmount,
        normalizedAmount,
        comparabilityScore,
        recencyWeight,
        finalWeight,
        isOutlier,
        wasIncluded,
        exclusionReason
    );

    private static decimal EnsureUnitInterval(decimal value, string fieldName)
    {
        if (value < 0m || value > 1m)
        {
            throw new InvalidOperationException($"{fieldName} debe estar entre 0 y 1.");
        }

        return value;
    }

    private static decimal EnsurePercent(decimal value, string fieldName)
    {
        if (value < 0m || value > 100m)
        {
            throw new InvalidOperationException($"{fieldName} debe estar entre 0 y 100.");
        }

        return value;
    }

    private static decimal? EnsureNonNegative(decimal? value, string fieldName)
    {
        if (value.HasValue && value.Value < 0m)
        {
            throw new InvalidOperationException($"{fieldName} no puede ser negativo.");
        }

        return value;
    }
}
