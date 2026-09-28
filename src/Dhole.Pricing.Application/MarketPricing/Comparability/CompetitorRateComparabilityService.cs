using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Domain.MarketPricing.Models;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.MarketPricing.Comparability;

internal sealed class CompetitorRateComparabilityService(
    ICompetitorRateObservationRepository observations,
    MarketComparabilityOptions options
) : ICompetitorRateComparabilityService
{
    private const string AlgorithmVersion = "market-comparability-v1";

    public async Task<MarketComparabilityResult> CompareAsync(
        MarketComparisonKey key,
        DateTime referenceDate,
        CancellationToken cancellationToken = default
    )
    {
        ValidateKey(key);
        options.Validate();

        var referenceUtc = NormalizeUtc(referenceDate);
        var oldestValidTo = referenceUtc.AddDays(-options.HistoricalLookbackDays);

        var candidates = await observations.GetMarketCandidatesAsync(
            key.Mode,
            key.PolId,
            referenceUtc,
            oldestValidTo,
            cancellationToken
        );

        return CompareCandidates(key, referenceUtc, candidates);
    }

    internal MarketComparabilityResult CompareCandidates(
        MarketComparisonKey key,
        DateTime referenceDate,
        IReadOnlyCollection<CompetitorRateObservation> candidates
    )
    {
        ValidateKey(key);
        options.Validate();

        var referenceUtc = NormalizeUtc(referenceDate);
        var evaluated = candidates
            .Select(observation => EvaluateObservation(key, referenceUtc, observation))
            .ToArray();

        var included = evaluated.Where(x => x.WasIncluded).ToArray();
        var competitorCount = CountCompetitors(included);
        var exactMatchCount = included.Count(x => x.IsExactMatch);
        var carrierExactMatchCount = included.Count(x => x.IsPrimaryCarrierMarket);
        var primaryCarrierCount = carrierExactMatchCount;
        var secondaryCarrierCount = included.Count(x =>
            x.CarrierMarket == MarketCarrierMarketKind.Secondary
        );

        var carrierFallbackUsed =
            key.CarrierId.HasValue
            && primaryCarrierCount < options.MinimumPrimaryCarrierObservations
            && secondaryCarrierCount > 0;

        var hasSufficientMarketData =
            competitorCount >= options.MinimumCompetitorsForSufficientMarket
            && included.Length >= options.MinimumObservationsForSufficientMarket;

        var confidenceBreakdown = CalculateConfidence(
            included,
            competitorCount,
            exactMatchCount,
            carrierExactMatchCount
        );
        var confidence = confidenceBreakdown.Score;

        return new MarketComparabilityResult(
            key,
            referenceUtc,
            candidates.Count,
            included.Length,
            competitorCount,
            exactMatchCount,
            carrierExactMatchCount,
            carrierFallbackUsed,
            hasSufficientMarketData,
            confidence,
            ResolveConfidenceLevel(confidence),
            confidenceBreakdown,
            AlgorithmVersion,
            evaluated
        );
    }

    private ComparableMarketObservation EvaluateObservation(
        MarketComparisonKey key,
        DateTime referenceDate,
        CompetitorRateObservation observation
    )
    {
        var hardFilterReason = ResolveHardFilterFailure(key, referenceDate, observation);
        if (hardFilterReason is not null)
        {
            return Excluded(observation, hardFilterReason);
        }

        var expired = observation.ValidTo.Date < referenceDate.Date;
        var carrierMarket = ResolveCarrierMarket(key, observation, expired);

        var incotermScore = key.IncotermId.HasValue
            ? observation.IncotermId == key.IncotermId ? 1m : 0m
            : observation.IncotermId.HasValue ? 0.50m : 0m;

        var routeScore = CalculateRouteScore(key, observation);

        var equipmentScore = key.ContainerTypeId.HasValue
            ? observation.ContainerTypeId == key.ContainerTypeId ? 1m : 0m
            : 1m;

        var modeScore = observation.Mode == key.Mode ? 1m : 0m;
        var carrierScore = CalculateCarrierScore(key, observation);
        var recencyWeight = CalculateRecencyWeight(referenceDate, observation);
        var extraction = Clamp01(observation.ExtractionConfidence);
        var normalization = Clamp01(observation.NormalizationConfidence);
        var completeness = CalculateCompleteness(key, observation);

        var scores = new MarketComparabilityDimensionScores(
            incotermScore,
            routeScore,
            equipmentScore,
            modeScore,
            carrierScore,
            recencyWeight,
            extraction,
            normalization,
            completeness
        );

        var structuralWeight = CalculateStructuralComparabilityWeight(scores);
        var comparabilityScore = CalculateFullComparabilityScore(scores);
        var finalWeight = decimal.Round(
            structuralWeight * recencyWeight * extraction * normalization,
            8,
            MidpointRounding.AwayFromZero
        );

        var exactMatch =
            incotermScore == 1m
            && routeScore == 1m
            && equipmentScore == 1m
            && modeScore == 1m
            && (!key.CarrierId.HasValue || carrierScore == 1m)
            && !expired;

        return new ComparableMarketObservation(
            observation,
            scores,
            structuralWeight,
            comparabilityScore,
            recencyWeight,
            finalWeight,
            carrierMarket,
            exactMatch,
            true,
            null
        );
    }

    private string? ResolveHardFilterFailure(
        MarketComparisonKey key,
        DateTime referenceDate,
        CompetitorRateObservation observation
    )
    {
        if (observation.Mode != key.Mode)
        {
            return "mode_mismatch";
        }

        if (observation.PolId != key.PolId)
        {
            return observation.PolId.HasValue ? "pol_mismatch" : "missing_pol";
        }

        if (
            key.ContainerTypeId.HasValue
            && observation.ContainerTypeId != key.ContainerTypeId
        )
        {
            return observation.ContainerTypeId.HasValue
                ? "equipment_mismatch"
                : "missing_equipment";
        }

        if (key.IncotermId.HasValue && observation.IncotermId != key.IncotermId)
        {
            return observation.IncotermId.HasValue
                ? "incoterm_mismatch"
                : "missing_incoterm";
        }

        if (
            key.PoeId.HasValue
            && observation.PoeId.HasValue
            && observation.PoeId != key.PoeId
        )
        {
            return "poe_mismatch";
        }

        if (
            key.PodId.HasValue
            && observation.PodId.HasValue
            && observation.PodId != key.PodId
        )
        {
            return "pod_mismatch";
        }

        if (observation.ValidFrom.Date > referenceDate.Date)
        {
            return "not_yet_valid";
        }

        if (!HasNormalizedUsdAmount(observation))
        {
            return "normalization_incomplete";
        }

        return null;
    }

    private decimal CalculateRouteScore(
        MarketComparisonKey key,
        CompetitorRateObservation observation
    )
    {
        var pol = observation.PolId == key.PolId ? 1m : 0m;
        var poe = CompareOptionalRouteDimension(key.PoeId, observation.PoeId);
        var pod = CompareOptionalRouteDimension(key.PodId, observation.PodId);

        return decimal.Round(
            (pol + poe + pod) / 3m,
            6,
            MidpointRounding.AwayFromZero
        );
    }

    private decimal CompareOptionalRouteDimension(Guid? requested, Guid? observed)
    {
        if (requested.HasValue)
        {
            if (observed == requested)
            {
                return 1m;
            }

            return observed.HasValue ? 0m : options.MissingRouteDimensionScore;
        }

        return observed.HasValue ? options.MoreSpecificRouteDimensionScore : 1m;
    }

    private decimal CalculateCarrierScore(
        MarketComparisonKey key,
        CompetitorRateObservation observation
    )
    {
        if (!key.CarrierId.HasValue)
        {
            return observation.CarrierId.HasValue ? 0.50m : options.MissingCarrierScore;
        }

        if (observation.CarrierId == key.CarrierId)
        {
            return 1m;
        }

        return observation.CarrierId.HasValue
            ? options.SecondaryCarrierScore
            : options.MissingCarrierScore;
    }

    private static MarketCarrierMarketKind ResolveCarrierMarket(
        MarketComparisonKey key,
        CompetitorRateObservation observation,
        bool expired
    )
    {
        if (expired)
        {
            return MarketCarrierMarketKind.Historical;
        }

        return key.CarrierId.HasValue && observation.CarrierId == key.CarrierId
            ? MarketCarrierMarketKind.Primary
            : MarketCarrierMarketKind.Secondary;
    }

    private decimal CalculateRecencyWeight(
        DateTime referenceDate,
        CompetitorRateObservation observation
    )
    {
        var ageDays = Math.Max(
            0d,
            (referenceDate.Date - observation.ValidFrom.Date).TotalDays
        );

        var weight = (decimal)Math.Exp(-ageDays / options.RecencyDecayDays);

        if (observation.ValidTo.Date < referenceDate.Date)
        {
            weight *= options.ExpiredRecencyPenalty;
        }

        return decimal.Round(
            Clamp01(weight),
            8,
            MidpointRounding.AwayFromZero
        );
    }

    private static decimal CalculateCompleteness(
        MarketComparisonKey key,
        CompetitorRateObservation observation
    )
    {
        var present = 0;
        var expected = 0;

        Add(observation.IncotermId.HasValue);
        Add(observation.PolId.HasValue);

        if (key.PoeId.HasValue)
            Add(observation.PoeId.HasValue);

        if (key.PodId.HasValue)
            Add(observation.PodId.HasValue);

        if (key.ContainerTypeId.HasValue)
            Add(observation.ContainerTypeId.HasValue);

        if (key.CarrierId.HasValue)
            Add(observation.CarrierId.HasValue);

        Add(
            string.Equals(
                observation.NormalizedCurrency,
                "USD",
                StringComparison.OrdinalIgnoreCase
            )
        );
        Add(HasComparableAmount(observation));

        return expected == 0
            ? 0m
            : decimal.Round((decimal)present / expected, 6, MidpointRounding.AwayFromZero);

        void Add(bool condition)
        {
            expected++;
            if (condition)
                present++;
        }
    }

    private decimal CalculateStructuralComparabilityWeight(
        MarketComparabilityDimensionScores scores
    )
    {
        var structuralTotal =
            options.IncotermWeight
            + options.RouteWeight
            + options.EquipmentWeight
            + options.ModeWeight
            + options.CarrierWeight
            + options.CompletenessWeight;

        var weighted =
            (scores.Incoterm * options.IncotermWeight)
            + (scores.Route * options.RouteWeight)
            + (scores.Equipment * options.EquipmentWeight)
            + (scores.Mode * options.ModeWeight)
            + (scores.Carrier * options.CarrierWeight)
            + (scores.Completeness * options.CompletenessWeight);

        return structuralTotal <= 0m
            ? 0m
            : decimal.Round(
                Clamp01(weighted / structuralTotal),
                8,
                MidpointRounding.AwayFromZero
            );
    }

    private decimal CalculateFullComparabilityScore(
        MarketComparabilityDimensionScores scores
    )
    {
        var score =
            (scores.Incoterm * options.IncotermWeight)
            + (scores.Route * options.RouteWeight)
            + (scores.Equipment * options.EquipmentWeight)
            + (scores.Mode * options.ModeWeight)
            + (scores.Carrier * options.CarrierWeight)
            + (scores.Recency * options.RecencyWeight)
            + (scores.Extraction * options.ExtractionWeight)
            + (scores.Normalization * options.NormalizationWeight)
            + (scores.Completeness * options.CompletenessWeight);

        return decimal.Round(
            Clamp01(score) * 100m,
            4,
            MidpointRounding.AwayFromZero
        );
    }

    private MarketConfidenceBreakdown CalculateConfidence(
        IReadOnlyCollection<ComparableMarketObservation> included,
        int competitorCount,
        int exactMatchCount,
        int carrierExactMatchCount
    )
    {
        if (included.Count == 0)
        {
            return new MarketConfidenceBreakdown(
                0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m
            );
        }

        var observationCount = included.Count;
        var competitorFactor = competitorCount switch
        {
            <= 0 => 0m,
            1 => 0.20m,
            2 => 0.45m,
            3 => 0.75m,
            4 => 0.90m,
            _ => 1m,
        };

        var observationFactor = observationCount switch
        {
            1 => 0.20m,
            2 => 0.45m,
            3 => 0.65m,
            4 or 5 => 0.85m,
            _ => 1m,
        };

        var exactRatio = (decimal)exactMatchCount / observationCount;
        var carrierExactRatio = (decimal)carrierExactMatchCount / observationCount;
        var completeness = included.Average(x => x.Scores.Completeness);
        var extraction = included.Average(x => x.Scores.Extraction);
        var normalization = included.Average(x => x.Scores.Normalization);
        var recency = included.Average(x => x.RecencyWeight);
        var primaryAvailability = carrierExactMatchCount > 0 ? 1m : 0m;

        var confidence =
            (competitorFactor * 0.20m)
            + (observationFactor * 0.10m)
            + (exactRatio * 0.15m)
            + (carrierExactRatio * 0.10m)
            + (completeness * 0.10m)
            + (extraction * 0.10m)
            + (normalization * 0.10m)
            + (recency * 0.10m)
            + (primaryAvailability * 0.05m);

        var score = decimal.Round(
            Clamp01(confidence) * 100m,
            4,
            MidpointRounding.AwayFromZero
        );

        if (competitorCount <= 1)
        {
            score = Math.Min(score, 39.99m);
        }
        else if (competitorCount == 2)
        {
            score = Math.Min(score, 59.99m);
        }

        return new MarketConfidenceBreakdown(
            competitorFactor,
            observationFactor,
            exactRatio,
            carrierExactRatio,
            completeness,
            extraction,
            normalization,
            recency,
            primaryAvailability,
            score
        );
    }

    private MarketConfidenceLevel ResolveConfidenceLevel(decimal confidence) =>
        confidence >= options.HighConfidenceThreshold
            ? MarketConfidenceLevel.High
            : confidence >= options.MediumConfidenceThreshold
                ? MarketConfidenceLevel.Medium
                : MarketConfidenceLevel.Low;

    private static int CountCompetitors(
        IReadOnlyCollection<ComparableMarketObservation> observations
    )
    {
        var competitors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in observations)
        {
            var observation = item.Observation;
            var key = observation.CompetitorCompanyId.HasValue
                ? $"id:{observation.CompetitorCompanyId.Value:D}"
                : $"name:{observation.CompetitorCompanyName.Trim()}";

            competitors.Add(key);
        }

        return competitors.Count;
    }

    private static ComparableMarketObservation Excluded(
        CompetitorRateObservation observation,
        string reason
    ) =>
        new(
            observation,
            new MarketComparabilityDimensionScores(0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m),
            0m,
            0m,
            0m,
            0m,
            MarketCarrierMarketKind.Secondary,
            false,
            false,
            reason
        );

    private static bool HasNormalizedUsdAmount(CompetitorRateObservation observation) =>
        string.Equals(
            observation.NormalizedCurrency,
            "USD",
            StringComparison.OrdinalIgnoreCase
        )
        && HasComparableAmount(observation);

    private static bool HasComparableAmount(CompetitorRateObservation observation) =>
        observation.NormalizedAllIn.HasValue
        || observation.NormalizedOceanFreight.HasValue
        || observation.NormalizedAmount.HasValue;

    private static decimal Clamp01(decimal value) => Math.Clamp(value, 0m, 1m);

    private static void ValidateKey(MarketComparisonKey key)
    {
        if (key.PolId == Guid.Empty)
            throw new InvalidOperationException("POL es obligatorio para comparar mercado.");

        if (!Enum.IsDefined(key.Mode))
            throw new InvalidOperationException("La modalidad solicitada no es válida.");

        if (!key.IncotermId.HasValue || key.IncotermId.Value == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Incoterm es obligatorio para construir un benchmark comparable."
            );
        }

        if (
            key.Mode is ShipmentMode.Fcl or ShipmentMode.Ftl
            && (!key.ContainerTypeId.HasValue || key.ContainerTypeId.Value == Guid.Empty)
        )
        {
            throw new InvalidOperationException(
                "El equipo es obligatorio para comparar mercado FCL/FTL."
            );
        }
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
