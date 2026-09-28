using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Domain.MarketPricing.Enums;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.MarketPricing.Normalization;

internal sealed class MarketRateNormalizationService(
    MarketCatalogNormalizer catalogNormalizer,
    MarketModeNormalizer modeNormalizer,
    MarketCurrencyNormalizer currencyNormalizer,
    MarketChargeNormalizer chargeNormalizer,
    IEnumerable<IMarketRateNormalizationStrategy> strategies
) : IMarketRateNormalizationService
{
    private static readonly string[] PortPolGroups = ["pol", "ports"];
    private static readonly string[] PortPoeGroups = ["poe", "ports"];
    private static readonly string[] PortPodGroups = ["pod", "ports"];
    private static readonly string[] IncotermGroups = ["incoterms"];
    private static readonly string[] CarrierGroups = ["carriers"];

    public async Task<MarketRateNormalizationResult> NormalizeAsync(
        MarketRateNormalizationRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var issues = new List<MarketNormalizationIssue>();
        var referenceDate = NormalizeUtc(request.ReferenceDate ?? DateTime.UtcNow);

        if (request.Quantity <= 0)
        {
            issues.Add(
                new MarketNormalizationIssue(
                    "invalid_quantity",
                    "La cantidad debe ser mayor que cero para normalizar la tarifa.",
                    true
                )
            );
        }

        var mode = modeNormalizer.Normalize(request.ModeHint, request.RawMode);
        if (!mode.HasValue)
        {
            issues.Add(
                new MarketNormalizationIssue(
                    "unresolved_mode",
                    $"No se pudo normalizar la modalidad '{request.RawMode ?? string.Empty}'.",
                    true
                )
            );
        }

        var incotermRaw = MarketTextNormalizer.ExtractIncoterm(request.Incoterm) ?? request.Incoterm;
        var incoterm = await catalogNormalizer.ResolveAsync(
            request.IncotermId,
            incotermRaw,
            "incoterms",
            IncotermGroups,
            value => MarketTextNormalizer.ExtractIncoterm(value)
                ?? MarketTextNormalizer.Normalize(value),
            cancellationToken
        );

        var pol = await catalogNormalizer.ResolveAsync(
            request.PolId,
            request.Pol,
            "pol",
            PortPolGroups,
            cancellationToken: cancellationToken
        );
        var poe = await catalogNormalizer.ResolveAsync(
            request.PoeId,
            request.Poe,
            "poe",
            PortPoeGroups,
            cancellationToken: cancellationToken
        );
        var pod = await catalogNormalizer.ResolveAsync(
            request.PodId,
            request.Pod,
            "pod",
            PortPodGroups,
            cancellationToken: cancellationToken
        );

        var carrier = await catalogNormalizer.ResolveAsync(
            request.CarrierId,
            request.Carrier,
            "carriers",
            CarrierGroups,
            value => MarketTextNormalizer.CanonicalCarrier(value),
            cancellationToken
        );

        var equipmentGroups = ResolveEquipmentGroups(mode);
        var equipment = await catalogNormalizer.ResolveAsync(
            request.EquipmentId,
            request.Equipment,
            equipmentGroups[0],
            equipmentGroups,
            value => MarketTextNormalizer.CanonicalEquipment(value),
            cancellationToken
        );

        AddCatalogIssue(
            issues,
            incoterm,
            "incoterm",
            required: true,
            missingCode: "missing_incoterm",
            unresolvedCode: "unresolved_incoterm"
        );
        AddCatalogIssue(
            issues,
            pol,
            "POL",
            required: true,
            missingCode: "missing_pol",
            unresolvedCode: "unresolved_pol"
        );
        AddCatalogIssue(
            issues,
            poe,
            "POE",
            required: false,
            missingCode: "missing_poe",
            unresolvedCode: "unresolved_poe"
        );
        AddCatalogIssue(
            issues,
            pod,
            "POD",
            required: false,
            missingCode: "missing_pod",
            unresolvedCode: "unresolved_pod"
        );
        AddCatalogIssue(
            issues,
            carrier,
            "naviera",
            required: false,
            missingCode: "missing_carrier",
            unresolvedCode: "unresolved_carrier"
        );

        var equipmentRequired = mode is ShipmentMode.Fcl or ShipmentMode.Ftl;
        AddCatalogIssue(
            issues,
            equipment,
            "equipo",
            required: equipmentRequired,
            missingCode: "missing_equipment",
            unresolvedCode: "unresolved_equipment"
        );

        var currency = await currencyNormalizer.NormalizeAsync(
            request.Currency,
            referenceDate,
            request.ExplicitExchangeRateToUsd,
            request.ExplicitExchangeRateDate,
            cancellationToken
        );

        if (!currency.Converted || !currency.RateToUsd.HasValue)
        {
            issues.Add(
                new MarketNormalizationIssue(
                    "currency_conversion_unavailable",
                    $"No existe una conversión determinística a USD para '{request.Currency}'.",
                    true
                )
            );
        }

        var (convertedCharges, chargeIssues) = await chargeNormalizer.NormalizeAsync(
            request,
            currency,
            cancellationToken
        );
        issues.AddRange(chargeIssues);

        if (convertedCharges.Count == 0)
        {
            issues.Add(
                new MarketNormalizationIssue(
                    "missing_charge_data",
                    "No existen montos de mercado que puedan normalizarse.",
                    true
                )
            );
        }

        MarketStrategyNormalizationResult strategyResult;
        if (mode.HasValue)
        {
            var strategy = strategies.FirstOrDefault(x => x.CanHandle(mode.Value));
            if (strategy is null)
            {
                issues.Add(
                    new MarketNormalizationIssue(
                        "normalization_strategy_not_found",
                        $"No existe estrategia de normalización para {mode.Value}.",
                        true
                    )
                );
                strategyResult = EmptyStrategyResult();
            }
            else
            {
                strategyResult = strategy.Normalize(
                    new MarketStrategyNormalizationContext(
                        request,
                        equipment,
                        convertedCharges
                    )
                );
                issues.AddRange(strategyResult.Issues);
            }
        }
        else
        {
            strategyResult = EmptyStrategyResult();
        }

        var normalizedCharges = strategyResult.Charges;
        var allInTotals = normalizedCharges.Where(x => x.IsAllInTotal).ToArray();

        if (allInTotals.Length > 1)
        {
            issues.Add(
                new MarketNormalizationIssue(
                    "multiple_all_in_totals",
                    "Se detectaron múltiples totales ALL IN; no se seleccionó uno automáticamente.",
                    true
                )
            );
        }

        var normalizedOceanFreight = SumCategory(
            normalizedCharges,
            MarketChargeCategory.OceanFreight
        );
        var normalizedOriginCharges = SumCategory(
            normalizedCharges,
            MarketChargeCategory.OriginCharges
        );
        var normalizedDestinationCharges = SumCategory(
            normalizedCharges,
            MarketChargeCategory.DestinationCharges
        );
        var normalizedInlandCharges = SumCategory(
            normalizedCharges,
            MarketChargeCategory.InlandCharges
        );
        var normalizedOtherCharges = SumCategory(
            normalizedCharges,
            MarketChargeCategory.OtherCharges
        );

        decimal? normalizedAllIn = null;
        if (allInTotals.Length == 1)
        {
            normalizedAllIn = allInTotals[0].NormalizedAmountUsd;
        }
        else if (allInTotals.Length == 0)
        {
            var componentCharges = normalizedCharges.Where(x => !x.IsAllInTotal).ToArray();
            if (componentCharges.Length > 0)
            {
                normalizedAllIn = decimal.Round(
                    componentCharges.Sum(x => x.NormalizedAmountUsd),
                    6,
                    MidpointRounding.AwayFromZero
                );
            }
        }

        var confidence = CalculateConfidence(
            mode,
            incoterm,
            pol,
            poe,
            pod,
            equipment,
            carrier,
            currency,
            strategyResult
        );

        return new MarketRateNormalizationResult(
            !issues.Any(x => x.IsBlocking),
            mode,
            incoterm,
            new NormalizedMarketRoute(pol, poe, pod),
            equipment,
            carrier,
            currency,
            strategyResult.TargetBasis,
            normalizedOceanFreight,
            normalizedOriginCharges,
            normalizedDestinationCharges,
            normalizedInlandCharges,
            normalizedOtherCharges,
            normalizedAllIn,
            confidence,
            normalizedCharges,
            issues
        );
    }

    private static string[] ResolveEquipmentGroups(ShipmentMode? mode) =>
        mode switch
        {
            ShipmentMode.Fcl or ShipmentMode.Lcl => ["container-types", "containers-types"],
            ShipmentMode.Ftl or ShipmentMode.Ltl => ["land-equipment-types"],
            ShipmentMode.Air or ShipmentMode.AirConsol => ["air-equipment-types"],
            _ => ["container-types", "containers-types", "land-equipment-types", "air-equipment-types"],
        };

    private static void AddCatalogIssue(
        ICollection<MarketNormalizationIssue> issues,
        MarketCatalogMatch match,
        string label,
        bool required,
        string missingCode,
        string unresolvedCode
    )
    {
        var hasInput = match.Id.HasValue || !string.IsNullOrWhiteSpace(match.RawValue);

        if (!hasInput)
        {
            if (required)
            {
                issues.Add(
                    new MarketNormalizationIssue(
                        missingCode,
                        $"No se recibió {label} para normalizar.",
                        true
                    )
                );
            }

            return;
        }

        if (match.Matched)
        {
            return;
        }

        issues.Add(
            new MarketNormalizationIssue(
                match.Ambiguous ? $"{unresolvedCode}_ambiguous" : unresolvedCode,
                match.Ambiguous
                    ? $"El valor '{match.RawValue}' coincide con más de una opción de {label}."
                    : $"No se pudo normalizar {label} '{match.RawValue}'.",
                required
            )
        );
    }

    private static decimal? SumCategory(
        IReadOnlyCollection<NormalizedMarketCharge> charges,
        MarketChargeCategory category
    )
    {
        var rows = charges.Where(x => !x.IsAllInTotal && x.Category == category).ToArray();
        if (rows.Length == 0)
        {
            return null;
        }

        return decimal.Round(
            rows.Sum(x => x.NormalizedAmountUsd),
            6,
            MidpointRounding.AwayFromZero
        );
    }

    private static decimal CalculateConfidence(
        ShipmentMode? mode,
        MarketCatalogMatch incoterm,
        MarketCatalogMatch pol,
        MarketCatalogMatch poe,
        MarketCatalogMatch pod,
        MarketCatalogMatch equipment,
        MarketCatalogMatch carrier,
        MarketCurrencyNormalization currency,
        MarketStrategyNormalizationResult strategy
    )
    {
        var values = new List<decimal>
        {
            mode.HasValue ? 1m : 0m,
            incoterm.Matched ? incoterm.Confidence : 0m,
            pol.Matched ? pol.Confidence : 0m,
            currency.Confidence,
            strategy.BasisConfidence,
        };

        AddOptionalConfidence(values, poe);
        AddOptionalConfidence(values, pod);
        AddOptionalConfidence(values, equipment);
        AddOptionalConfidence(values, carrier);

        return values.Count == 0
            ? 0m
            : decimal.Round(values.Average(), 6, MidpointRounding.AwayFromZero);
    }

    private static void AddOptionalConfidence(
        ICollection<decimal> values,
        MarketCatalogMatch match
    )
    {
        if (!match.Id.HasValue && string.IsNullOrWhiteSpace(match.RawValue))
        {
            return;
        }

        values.Add(match.Matched ? match.Confidence : 0m);
    }

    private static MarketStrategyNormalizationResult EmptyStrategyResult() =>
        new(
            MarketRateBasis.Unknown,
            Array.Empty<NormalizedMarketCharge>(),
            0m,
            Array.Empty<MarketNormalizationIssue>()
        );

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
