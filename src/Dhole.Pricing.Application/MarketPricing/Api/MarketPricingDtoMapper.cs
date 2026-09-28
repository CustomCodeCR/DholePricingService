using Dhole.Pricing.Application.MarketPricing.AutoPricing;
using Dhole.Pricing.Contracts.MarketPricing.Response;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Domain.MarketPricing.Models;

namespace Dhole.Pricing.Application.MarketPricing.Api;

internal static class MarketPricingDtoMapper
{
    public static MarketBenchmarkDto ToDto(
        MarketBenchmarkResult benchmark,
        IReadOnlyCollection<CompetitorRateObservation> observationEntities
    )
    {
        var lookup = observationEntities.ToDictionary(x => x.Id);

        return new MarketBenchmarkDto(
            benchmark.CandidateCount,
            benchmark.ObservationCount,
            benchmark.CompetitorCount,
            benchmark.OutlierCount,
            benchmark.Average,
            benchmark.WeightedAverage,
            benchmark.Median,
            benchmark.Minimum,
            benchmark.Maximum,
            benchmark.StandardDeviation,
            benchmark.Percentile25,
            benchmark.Percentile40,
            benchmark.Percentile50,
            benchmark.Percentile60,
            benchmark.Percentile65,
            benchmark.Percentile75,
            benchmark.LowerMarket,
            benchmark.UpperMarket,
            benchmark.TargetPercentile,
            benchmark.CompetitiveCeilingPercentile,
            benchmark.TargetMarketPrice,
            benchmark.CompetitiveCeiling,
            benchmark.ConfidenceScore,
            benchmark.HasSufficientMarketData,
            benchmark.CarrierFallbackUsed,
            benchmark.AlgorithmVersion,
            benchmark.Observations
                .Select(x => ToDto(x, lookup.GetValueOrDefault(x.CompetitorRateObservationId)))
                .ToArray()
        );
    }

    public static AutoPricingCalculationDto ToDto(
        Guid decisionId,
        AutoPricingProposal proposal,
        IReadOnlyCollection<CompetitorRateObservation> observationEntities
    ) =>
        new(
            decisionId,
            proposal.RateId,
            proposal.Status.ToString(),
            proposal.ApplicationMode.ToString(),
            proposal.CanApply,
            proposal.ShouldAutoApply,
            proposal.RequiresReview,
            proposal.DesiredSalePrice,
            proposal.AppliedAdjustmentAmount,
            proposal.UnallocatedAdjustmentAmount,
            proposal.MarketDeviationPercentage,
            proposal.AlgorithmVersion,
            new AutoPricingPositionDto(
                proposal.Position.Cost,
                proposal.Position.OriginalSale,
                proposal.Position.MinimumSalePrice,
                proposal.Position.MarketMedian,
                proposal.Position.WeightedMarketAverage,
                proposal.Position.TargetMarketPrice,
                proposal.Position.CompetitiveCeiling,
                proposal.Position.SuggestedSalePrice,
                proposal.Position.CurrentMargin,
                proposal.Position.SuggestedMargin,
                proposal.Position.AvailableHeadroom,
                proposal.Position.ConfidenceScore,
                proposal.Position.Status.ToString()
            ),
            ToDto(proposal.Benchmark, observationEntities),
            proposal.Adjustments
                .Select(x => new AutoPricingAdjustmentDto(
                    x.RateDetailId,
                    x.ChargeCode,
                    x.CurrencyCode,
                    x.Quantity,
                    x.OriginalUnitSaleAmount,
                    x.SuggestedUnitSaleAmount,
                    x.OriginalTotalSaleUsd,
                    x.SuggestedTotalSaleUsd,
                    x.AdjustmentAmountUsd,
                    x.WasAdjusted,
                    x.IsProtected,
                    x.AdjustmentPriority,
                    x.AdjustmentStrategy?.ToString(),
                    x.ProtectionReason
                ))
                .ToArray(),
            proposal.ValidationIssues
                .Select(x => new AutoPricingValidationIssueDto(
                    x.Code,
                    x.Message,
                    x.IsBlocking
                ))
                .ToArray()
        );

    public static AutoPricingDecisionDto ToDto(
        PricingMarketDecision decision,
        IReadOnlyCollection<PricingMarketDecisionObservation> observations,
        IReadOnlyCollection<CompetitorRateObservation> observationEntities
    )
    {
        var lookup = observationEntities.ToDictionary(x => x.Id);

        return new AutoPricingDecisionDto(
            decision.Id,
            decision.RateId,
            decision.CalculatedAtUtc,
            decision.ComparisonIncotermId,
            decision.ComparisonPolId,
            decision.ComparisonPoeId,
            decision.ComparisonPodId,
            decision.ComparisonContainerTypeId,
            decision.ComparisonMode.ToString(),
            decision.ComparisonCarrierId,
            decision.CostTotal,
            decision.OriginalSaleTotal,
            decision.SuggestedSaleTotal,
            decision.FinalSaleTotal,
            decision.Average,
            decision.WeightedAverage,
            decision.Median,
            decision.P25,
            decision.P40,
            decision.P50,
            decision.P60,
            decision.P65,
            decision.P75,
            decision.TargetMarketPrice,
            decision.CompetitiveCeiling,
            decision.CompetitorCount,
            decision.ObservationCount,
            decision.ConfidenceScore,
            decision.AlgorithmVersion,
            decision.WasAutoApplied,
            decision.WasManuallyModified,
            decision.ReviewedByUserId,
            decision.ReviewedAtUtc,
            decision.CreatedAtUtc,
            observations
                .Select(row =>
                    ToDto(
                        row,
                        lookup.GetValueOrDefault(row.CompetitorRateObservationId),
                        decision.ComparisonCarrierId
                    )
                )
                .ToArray()
        );
    }

    private static MarketBenchmarkObservationDto ToDto(
        MarketObservationResult observation,
        CompetitorRateObservation? entity
    ) =>
        new(
            observation.CompetitorRateObservationId,
            observation.CompetitorCompanyId,
            observation.CompetitorCompanyName,
            entity?.IncotermId,
            entity?.IncotermCode,
            entity?.PolId,
            entity?.PolName,
            entity?.PolCode,
            entity?.PoeId,
            entity?.PoeName,
            entity?.PoeCode,
            entity?.PodId,
            entity?.PodName,
            entity?.PodCode,
            entity?.ContainerTypeId,
            entity?.ContainerTypeCode,
            entity?.Mode.ToString() ?? string.Empty,
            entity?.CarrierId,
            entity?.CarrierName,
            entity?.CarrierCode,
            entity?.ValidFrom ?? default,
            entity?.ValidTo ?? default,
            observation.OriginalAmount,
            observation.NormalizedAmount,
            observation.ComparabilityScore,
            observation.RecencyWeight,
            observation.FinalWeight,
            observation.IsPrimaryCarrierMarket,
            observation.IsOutlier,
            observation.WasIncluded,
            observation.ExclusionReason,
            observation.OutlierReason
        );

    private static MarketBenchmarkObservationDto ToDto(
        PricingMarketDecisionObservation row,
        CompetitorRateObservation? entity,
        Guid? comparisonCarrierId
    ) =>
        new(
            row.CompetitorRateObservationId,
            row.CompetitorCompanyId,
            entity?.CompetitorCompanyName ?? string.Empty,
            entity?.IncotermId,
            entity?.IncotermCode,
            entity?.PolId,
            entity?.PolName,
            entity?.PolCode,
            entity?.PoeId,
            entity?.PoeName,
            entity?.PoeCode,
            entity?.PodId,
            entity?.PodName,
            entity?.PodCode,
            entity?.ContainerTypeId,
            entity?.ContainerTypeCode,
            entity?.Mode.ToString() ?? string.Empty,
            entity?.CarrierId,
            entity?.CarrierName,
            entity?.CarrierCode,
            entity?.ValidFrom ?? default,
            entity?.ValidTo ?? default,
            row.OriginalAmount,
            row.NormalizedAmount,
            row.ComparabilityScore,
            row.RecencyWeight,
            row.FinalWeight,
            comparisonCarrierId.HasValue && entity?.CarrierId == comparisonCarrierId,
            row.IsOutlier,
            row.WasIncluded,
            row.ExclusionReason,
            row.IsOutlier ? row.ExclusionReason : null
        );
}
