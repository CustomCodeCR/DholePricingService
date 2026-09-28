using CustomCodeFramework.Core.Domain.Entities;
using Dhole.Pricing.Domain.MarketPricing.Models;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Domain.MarketPricing.Entities;

public sealed class PricingMarketDecision : Entity<Guid>
{
    private PricingMarketDecision() { }

    private PricingMarketDecision(
        Guid id,
        Guid rateId,
        MarketComparisonKey comparisonKey,
        decimal costTotal,
        decimal originalSaleTotal,
        decimal suggestedSaleTotal,
        decimal finalSaleTotal,
        decimal? average,
        decimal? weightedAverage,
        decimal? median,
        decimal? p25,
        decimal? p40,
        decimal? p50,
        decimal? p60,
        decimal? p65,
        decimal? p75,
        decimal? targetMarketPrice,
        decimal? competitiveCeiling,
        int competitorCount,
        int observationCount,
        decimal confidenceScore,
        string algorithmVersion,
        bool wasAutoApplied
    ) : base(id)
    {
        if (rateId == Guid.Empty)
        {
            throw new InvalidOperationException("La tarifa asociada a la decisión de mercado es obligatoria.");
        }

        if (comparisonKey.PolId == Guid.Empty)
        {
            throw new InvalidOperationException("El POL es obligatorio para la clave de comparación.");
        }

        if (competitorCount < 0 || observationCount < 0)
        {
            throw new InvalidOperationException("Los conteos del benchmark no pueden ser negativos.");
        }

        if (confidenceScore < 0m || confidenceScore > 100m)
        {
            throw new InvalidOperationException("La confianza del benchmark debe estar entre 0 y 100.");
        }

        if (string.IsNullOrWhiteSpace(algorithmVersion))
        {
            throw new InvalidOperationException("La versión del algoritmo es obligatoria.");
        }

        RateId = rateId;
        CalculatedAtUtc = DateTime.UtcNow;

        ComparisonIncotermId = comparisonKey.IncotermId;
        ComparisonPolId = comparisonKey.PolId;
        ComparisonPoeId = comparisonKey.PoeId;
        ComparisonPodId = comparisonKey.PodId;
        ComparisonContainerTypeId = comparisonKey.ContainerTypeId;
        ComparisonMode = comparisonKey.Mode;
        ComparisonCarrierId = comparisonKey.CarrierId;

        CostTotal = costTotal;
        OriginalSaleTotal = originalSaleTotal;
        SuggestedSaleTotal = suggestedSaleTotal;
        FinalSaleTotal = finalSaleTotal;

        Average = average;
        WeightedAverage = weightedAverage;
        Median = median;
        P25 = p25;
        P40 = p40;
        P50 = p50;
        P60 = p60;
        P65 = p65;
        P75 = p75;

        TargetMarketPrice = targetMarketPrice;
        CompetitiveCeiling = competitiveCeiling;

        CompetitorCount = competitorCount;
        ObservationCount = observationCount;
        ConfidenceScore = confidenceScore;

        AlgorithmVersion = algorithmVersion.Trim();
        WasAutoApplied = wasAutoApplied;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid RateId { get; private set; }
    public DateTime CalculatedAtUtc { get; private set; }

    public Guid? ComparisonIncotermId { get; private set; }
    public Guid ComparisonPolId { get; private set; }
    public Guid? ComparisonPoeId { get; private set; }
    public Guid? ComparisonPodId { get; private set; }
    public Guid? ComparisonContainerTypeId { get; private set; }
    public ShipmentMode ComparisonMode { get; private set; }
    public Guid? ComparisonCarrierId { get; private set; }

    public decimal CostTotal { get; private set; }
    public decimal OriginalSaleTotal { get; private set; }
    public decimal SuggestedSaleTotal { get; private set; }
    public decimal FinalSaleTotal { get; private set; }

    public decimal? Average { get; private set; }
    public decimal? WeightedAverage { get; private set; }
    public decimal? Median { get; private set; }
    public decimal? P25 { get; private set; }
    public decimal? P40 { get; private set; }
    public decimal? P50 { get; private set; }
    public decimal? P60 { get; private set; }
    public decimal? P65 { get; private set; }
    public decimal? P75 { get; private set; }

    public decimal? TargetMarketPrice { get; private set; }
    public decimal? CompetitiveCeiling { get; private set; }

    public int CompetitorCount { get; private set; }
    public int ObservationCount { get; private set; }
    public decimal ConfidenceScore { get; private set; }

    public string AlgorithmVersion { get; private set; } = string.Empty;

    public bool WasAutoApplied { get; private set; }
    public bool WasManuallyModified { get; private set; }

    public Guid? ReviewedByUserId { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public static PricingMarketDecision Create(
        Guid rateId,
        MarketComparisonKey comparisonKey,
        decimal costTotal,
        decimal originalSaleTotal,
        decimal suggestedSaleTotal,
        decimal finalSaleTotal,
        decimal? average,
        decimal? weightedAverage,
        decimal? median,
        decimal? p25,
        decimal? p40,
        decimal? p50,
        decimal? p60,
        decimal? p65,
        decimal? p75,
        decimal? targetMarketPrice,
        decimal? competitiveCeiling,
        int competitorCount,
        int observationCount,
        decimal confidenceScore,
        string algorithmVersion,
        bool wasAutoApplied
    ) => new(
        Guid.NewGuid(),
        rateId,
        comparisonKey,
        costTotal,
        originalSaleTotal,
        suggestedSaleTotal,
        finalSaleTotal,
        average,
        weightedAverage,
        median,
        p25,
        p40,
        p50,
        p60,
        p65,
        p75,
        targetMarketPrice,
        competitiveCeiling,
        competitorCount,
        observationCount,
        confidenceScore,
        algorithmVersion,
        wasAutoApplied
    );

    public void MarkAutoApplied(decimal finalSaleTotal)
    {
        FinalSaleTotal = finalSaleTotal;
        WasAutoApplied = true;
    }

    public void MarkManualOverride(decimal finalSaleTotal)
    {
        FinalSaleTotal = finalSaleTotal;
        WasManuallyModified = true;
    }

    public void MarkReviewed(Guid userId, DateTime? reviewedAtUtc = null)
    {
        if (userId == Guid.Empty)
        {
            throw new InvalidOperationException("El usuario revisor es obligatorio.");
        }

        ReviewedByUserId = userId;
        ReviewedAtUtc = NormalizeUtc(reviewedAtUtc ?? DateTime.UtcNow);
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
