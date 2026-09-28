using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.MarketPricing.Comparability;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Domain.MarketPricing.Models;

namespace Dhole.Pricing.Application.MarketPricing.Benchmark;

internal sealed class MarketBenchmarkService(
    ICompetitorRateComparabilityService comparabilityService,
    MarketBenchmarkOptions options
) : IMarketBenchmarkService
{
    private const string AlgorithmVersion = "market-benchmark-v1";
    private const decimal ModifiedZScale = 0.6745m;

    public async Task<MarketBenchmarkResult> CalculateAsync(
        MarketBenchmarkRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ValidateRequest(request);
        options.Validate();

        var comparison = await comparabilityService.CompareAsync(
            request.Key,
            request.ReferenceDate,
            cancellationToken
        );

        var evaluated = comparison.Observations
            .Select(x => new EvaluatedObservation(
                x,
                ResolveNormalizedAmount(x.Observation, request.AmountKind),
                ResolveOriginalAmount(x.Observation, request.AmountKind)
            ))
            .ToArray();

        var comparable = evaluated
            .Where(x => x.Comparison.WasIncluded && x.NormalizedAmount.HasValue)
            .ToArray();

        var outlierAnalysis = DetectOutliers(comparable);
        var outlierIds = outlierAnalysis.ToDictionary(
            x => x.ObservationId,
            x => x.Reason
        );

        var benchmarkRows = comparable
            .Where(x => !outlierIds.ContainsKey(x.Comparison.Observation.Id))
            .ToArray();

        var values = benchmarkRows
            .Select(x => x.NormalizedAmount!.Value)
            .OrderBy(x => x)
            .ToArray();

        decimal? average = values.Length > 0
            ? RoundMoney(values.Average())
            : null;
        var weightedAverage = CalculateWeightedAverage(benchmarkRows);
        decimal? median = values.Length > 0 ? RoundMoney(Percentile(values, 50m)) : null;
        decimal? minimum = values.Length > 0 ? RoundMoney(values[0]) : null;
        decimal? maximum = values.Length > 0 ? RoundMoney(values[^1]) : null;
        decimal? standardDeviation = values.Length > 0
            ? RoundMoney(StandardDeviation(values))
            : null;

        var p25 = GetPercentile(values, 25m);
        var p40 = GetPercentile(values, 40m);
        var p50 = GetPercentile(values, 50m);
        var p60 = GetPercentile(values, 60m);
        var p65 = GetPercentile(values, 65m);
        var p75 = GetPercentile(values, 75m);

        decimal? targetMarketPrice = values.Length > 0
            ? RoundMoney(Percentile(values, request.TargetPercentile))
            : null;
        decimal? competitiveCeiling = values.Length > 0
            ? RoundMoney(Percentile(values, request.CompetitiveCeilingPercentile))
            : null;

        var competitorCount = CountCompetitors(benchmarkRows);
        var confidence = CalculateBenchmarkConfidence(
            comparison.ConfidenceScore,
            values,
            outlierIds.Count,
            comparable.Length,
            competitorCount
        );

        var primaryCount = benchmarkRows.Count(x => x.Comparison.IsPrimaryCarrierMarket);
        var secondaryCount = benchmarkRows.Count(x =>
            x.Comparison.CarrierMarket == MarketCarrierMarketKind.Secondary
        );
        var carrierFallbackUsed =
            comparison.CarrierFallbackUsed
            || (
                request.Key.CarrierId.HasValue
                && primaryCount == 0
                && secondaryCount > 0
            );

        var hasSufficientMarketData =
            comparison.HasSufficientMarketData
            && benchmarkRows.Length >= 2
            && competitorCount >= 2;

        var observations = evaluated
            .Select(item => ToMarketObservationResult(item, outlierIds))
            .ToArray();

        return new MarketBenchmarkResult(
            ObservationCount: benchmarkRows.Length,
            CompetitorCount: competitorCount,
            CandidateCount: comparison.CandidateCount,
            OutlierCount: outlierIds.Count,
            Average: average,
            WeightedAverage: weightedAverage,
            Median: median,
            Minimum: minimum,
            Maximum: maximum,
            StandardDeviation: standardDeviation,
            Percentile25: p25,
            Percentile40: p40,
            Percentile50: p50,
            Percentile60: p60,
            Percentile65: p65,
            Percentile75: p75,
            LowerMarket: p25,
            UpperMarket: p75,
            TargetPercentile: request.TargetPercentile,
            CompetitiveCeilingPercentile: request.CompetitiveCeilingPercentile,
            TargetMarketPrice: targetMarketPrice,
            CompetitiveCeiling: competitiveCeiling,
            ConfidenceScore: confidence,
            HasSufficientMarketData: hasSufficientMarketData,
            CarrierFallbackUsed: carrierFallbackUsed,
            AlgorithmVersion: $"{comparison.AlgorithmVersion}+{AlgorithmVersion}",
            Observations: observations
        );
    }

    private IReadOnlyCollection<OutlierDetection> DetectOutliers(
        IReadOnlyCollection<EvaluatedObservation> comparable
    )
    {
        if (comparable.Count < options.MinimumOutlierSampleSize)
        {
            return Array.Empty<OutlierDetection>();
        }

        var values = comparable
            .Select(x => x.NormalizedAmount!.Value)
            .OrderBy(x => x)
            .ToArray();

        var median = Percentile(values, 50m);
        var deviations = values
            .Select(x => Math.Abs(x - median))
            .OrderBy(x => x)
            .ToArray();
        var mad = Percentile(deviations, 50m);

        var q1 = Percentile(values, 25m);
        var q3 = Percentile(values, 75m);
        var iqr = q3 - q1;
        var lowerFence = q1 - (options.IqrMultiplier * iqr);
        var upperFence = q3 + (options.IqrMultiplier * iqr);

        var outliers = new List<OutlierDetection>();

        foreach (var item in comparable)
        {
            var value = item.NormalizedAmount!.Value;
            var reasons = new List<string>();

            if (mad > 0m)
            {
                var modifiedZ = ModifiedZScale * Math.Abs(value - median) / mad;
                if (modifiedZ > options.MadModifiedZThreshold)
                {
                    reasons.Add(
                        $"MAD modified-z {modifiedZ:0.####} > {options.MadModifiedZThreshold:0.####}"
                    );
                }
            }

            if (iqr > 0m && (value < lowerFence || value > upperFence))
            {
                reasons.Add(
                    $"IQR fuera de [{lowerFence:0.####}, {upperFence:0.####}]"
                );
            }

            if (reasons.Count > 0)
            {
                outliers.Add(
                    new OutlierDetection(
                        item.Comparison.Observation.Id,
                        string.Join("; ", reasons)
                    )
                );
            }
        }

        return outliers;
    }

    private decimal? CalculateWeightedAverage(
        IReadOnlyCollection<EvaluatedObservation> rows
    )
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var totalWeight = rows.Sum(x => Math.Max(0m, x.Comparison.FinalWeight));
        if (totalWeight <= 0m)
        {
            return null;
        }

        var weightedTotal = rows.Sum(x =>
            x.NormalizedAmount!.Value * Math.Max(0m, x.Comparison.FinalWeight)
        );

        return RoundMoney(weightedTotal / totalWeight);
    }

    private decimal CalculateBenchmarkConfidence(
        decimal comparabilityConfidence,
        IReadOnlyCollection<decimal> values,
        int outlierCount,
        int comparableCount,
        int competitorCount
    )
    {
        if (values.Count == 0)
        {
            return 0m;
        }

        var median = Percentile(values.OrderBy(x => x).ToArray(), 50m);
        var standardDeviation = StandardDeviation(values);
        var coefficientOfVariation =
            median == 0m
                ? standardDeviation == 0m ? 0m : 1m
                : Math.Abs(standardDeviation / median);

        var dispersionFactor =
            coefficientOfVariation <= options.LowDispersionCvThreshold
                ? options.LowDispersionConfidenceFactor
                : coefficientOfVariation <= options.ModerateDispersionCvThreshold
                    ? options.ModerateDispersionConfidenceFactor
                    : coefficientOfVariation <= options.HighDispersionCvThreshold
                        ? options.HighDispersionConfidenceFactor
                        : coefficientOfVariation <= options.VeryHighDispersionCvThreshold
                            ? options.VeryHighDispersionConfidenceFactor
                            : options.ExtremeDispersionConfidenceFactor;

        var outlierRatio = comparableCount <= 0
            ? 0m
            : (decimal)outlierCount / comparableCount;
        var outlierPenalty = Math.Min(
            options.MaximumOutlierConfidencePenalty,
            outlierRatio * 0.50m
        );
        var outlierFactor = 1m - outlierPenalty;

        var score = Math.Clamp(
            comparabilityConfidence * dispersionFactor * outlierFactor,
            0m,
            100m
        );

        if (competitorCount <= 1)
        {
            score = Math.Min(score, 39.99m);
        }
        else if (competitorCount == 2)
        {
            score = Math.Min(score, 59.99m);
        }

        if (values.Count == 1)
        {
            score = Math.Min(score, 39.99m);
        }

        return decimal.Round(score, 4, MidpointRounding.AwayFromZero);
    }

    private static MarketObservationResult ToMarketObservationResult(
        EvaluatedObservation item,
        IReadOnlyDictionary<Guid, string> outliers
    )
    {
        var observation = item.Comparison.Observation;
        var isOutlier = outliers.TryGetValue(observation.Id, out var outlierReason);
        var missingBenchmarkAmount =
            item.Comparison.WasIncluded && !item.NormalizedAmount.HasValue;

        string? exclusionReason = item.Comparison.ExclusionReason;
        if (missingBenchmarkAmount)
        {
            exclusionReason = "benchmark_amount_unavailable";
        }
        else if (isOutlier)
        {
            exclusionReason = "outlier";
        }

        return new MarketObservationResult(
            observation.Id,
            observation.CompetitorCompanyId,
            observation.CompetitorCompanyName,
            item.OriginalAmount,
            item.NormalizedAmount,
            item.Comparison.ComparabilityScore,
            item.Comparison.RecencyWeight,
            item.Comparison.FinalWeight,
            item.Comparison.IsPrimaryCarrierMarket,
            isOutlier,
            item.Comparison.WasIncluded
                && item.NormalizedAmount.HasValue
                && !isOutlier,
            exclusionReason,
            isOutlier ? outlierReason : null
        );
    }

    private static decimal? ResolveNormalizedAmount(
        CompetitorRateObservation observation,
        MarketBenchmarkAmountKind amountKind
    ) =>
        amountKind switch
        {
            MarketBenchmarkAmountKind.AllIn => observation.NormalizedAllIn,
            MarketBenchmarkAmountKind.OceanFreight => observation.NormalizedOceanFreight,
            MarketBenchmarkAmountKind.NormalizedAmount => observation.NormalizedAmount,
            _ => null,
        };

    private static decimal? ResolveOriginalAmount(
        CompetitorRateObservation observation,
        MarketBenchmarkAmountKind amountKind
    ) =>
        amountKind switch
        {
            MarketBenchmarkAmountKind.OceanFreight => observation.OceanFreight,
            MarketBenchmarkAmountKind.NormalizedAmount => observation.OriginalAmount,
            MarketBenchmarkAmountKind.AllIn =>
                observation.OriginalAmount ?? SumRawComponents(observation),
            _ => null,
        };

    private static decimal? SumRawComponents(CompetitorRateObservation observation)
    {
        var values = new[]
        {
            observation.OceanFreight,
            observation.OriginCharges,
            observation.DestinationCharges,
            observation.InlandCharges,
            observation.OtherCharges,
        };

        if (!values.Any(x => x.HasValue))
        {
            return null;
        }

        return values.Where(x => x.HasValue).Sum(x => x!.Value);
    }

    private static decimal? GetPercentile(decimal[] sortedValues, decimal percentile) =>
        sortedValues.Length == 0
            ? null
            : RoundMoney(Percentile(sortedValues, percentile));

    internal static decimal Percentile(decimal[] sortedValues, decimal percentile)
    {
        if (sortedValues.Length == 0)
            throw new InvalidOperationException("No se puede calcular un percentil sin observaciones.");

        if (percentile < 0m || percentile > 100m)
            throw new InvalidOperationException("El percentil debe estar entre 0 y 100.");

        if (sortedValues.Length == 1)
            return sortedValues[0];

        var position = (double)(percentile / 100m) * (sortedValues.Length - 1);
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);

        if (lowerIndex == upperIndex)
            return sortedValues[lowerIndex];

        var fraction = (decimal)(position - lowerIndex);
        return sortedValues[lowerIndex]
            + ((sortedValues[upperIndex] - sortedValues[lowerIndex]) * fraction);
    }

    internal static decimal StandardDeviation(IReadOnlyCollection<decimal> values)
    {
        if (values.Count <= 1)
            return 0m;

        var mean = values.Average();
        var variance = values.Average(value =>
        {
            var delta = value - mean;
            return delta * delta;
        });

        return (decimal)Math.Sqrt((double)variance);
    }

    private static int CountCompetitors(
        IReadOnlyCollection<EvaluatedObservation> observations
    )
    {
        var competitors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in observations)
        {
            var observation = item.Comparison.Observation;
            var key = observation.CompetitorCompanyId.HasValue
                ? $"id:{observation.CompetitorCompanyId.Value:D}"
                : $"name:{observation.CompetitorCompanyName.Trim()}";

            competitors.Add(key);
        }

        return competitors.Count;
    }

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero);

    private static void ValidateRequest(MarketBenchmarkRequest request)
    {
        if (!Enum.IsDefined(request.AmountKind))
            throw new InvalidOperationException("El tipo de monto del benchmark no es válido.");

        if (request.TargetPercentile < 0m || request.TargetPercentile > 100m)
            throw new InvalidOperationException("TargetPercentile debe estar entre 0 y 100.");

        if (
            request.CompetitiveCeilingPercentile < 0m
            || request.CompetitiveCeilingPercentile > 100m
        )
        {
            throw new InvalidOperationException(
                "CompetitiveCeilingPercentile debe estar entre 0 y 100."
            );
        }

        if (request.CompetitiveCeilingPercentile < request.TargetPercentile)
        {
            throw new InvalidOperationException(
                "El percentil de techo competitivo no puede ser menor que el percentil objetivo."
            );
        }
    }

    private sealed record EvaluatedObservation(
        ComparableMarketObservation Comparison,
        decimal? NormalizedAmount,
        decimal? OriginalAmount
    );

    private sealed record OutlierDetection(Guid ObservationId, string Reason);
}
