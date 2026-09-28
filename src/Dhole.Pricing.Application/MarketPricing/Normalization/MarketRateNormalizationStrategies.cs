using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Domain.MarketPricing.Enums;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.MarketPricing.Normalization;

internal abstract class MarketRateNormalizationStrategyBase : IMarketRateNormalizationStrategy
{
    public abstract bool CanHandle(ShipmentMode mode);

    public abstract MarketStrategyNormalizationResult Normalize(
        MarketStrategyNormalizationContext context
    );

    protected static MarketStrategyNormalizationResult NormalizeCharges(
        MarketStrategyNormalizationContext context,
        MarketRateBasis targetBasis,
        Func<ConvertedMarketCharge, ConversionResult> converter
    )
    {
        var charges = new List<NormalizedMarketCharge>();
        var issues = new List<MarketNormalizationIssue>();
        var confidences = new List<decimal>();

        foreach (var charge in context.Charges)
        {
            var converted = converter(charge);
            confidences.Add(converted.Confidence);

            if (!converted.Success)
            {
                issues.Add(
                    new MarketNormalizationIssue(
                        converted.IssueCode ?? "unsupported_rate_basis",
                        converted.Message
                            ?? $"No se pudo convertir '{charge.Name}' desde {charge.SourceBasis} a {targetBasis}.",
                        true
                    )
                );
                continue;
            }

            charges.Add(
                new NormalizedMarketCharge(
                    charge.Name,
                    charge.Category,
                    charge.SourceBasis,
                    targetBasis,
                    charge.OriginalAmount,
                    charge.OriginalCurrency,
                    decimal.Round(converted.Amount, 6, MidpointRounding.AwayFromZero),
                    charge.CurrencyRateToUsd,
                    converted.Confidence,
                    charge.IsAllInTotal
                )
            );
        }

        var confidence = confidences.Count == 0
            ? 0m
            : decimal.Round(confidences.Average(), 6, MidpointRounding.AwayFromZero);

        return new MarketStrategyNormalizationResult(
            targetBasis,
            charges,
            confidence,
            issues
        );
    }

    protected readonly record struct ConversionResult(
        bool Success,
        decimal Amount,
        decimal Confidence,
        string? IssueCode = null,
        string? Message = null
    )
    {
        public static ConversionResult Exact(decimal amount) => new(true, amount, 1m);

        public static ConversionResult Assumed(decimal amount, decimal confidence, string message) =>
            new(true, amount, confidence, "assumed_rate_basis", message);

        public static ConversionResult Failure(string code, string message) =>
            new(false, 0m, 0m, code, message);
    }

    protected static decimal? ResolveWeightOrMeasureUnits(MarketRateNormalizationRequest request)
    {
        var volume = request.VolumeCbm.GetValueOrDefault();
        var tons = request.WeightKg.GetValueOrDefault() / 1000m;
        var units = Math.Max(volume, tons);

        return units > 0m ? units : null;
    }

    protected static decimal? ResolveAirWeightKg(MarketRateNormalizationRequest request)
    {
        var value = request.ChargeableWeightKg ?? request.WeightKg;
        return value.HasValue && value.Value > 0m ? value.Value : null;
    }
}

internal sealed class FclMarketRateNormalizationStrategy : MarketRateNormalizationStrategyBase
{
    public override bool CanHandle(ShipmentMode mode) => mode == ShipmentMode.Fcl;

    public override MarketStrategyNormalizationResult Normalize(
        MarketStrategyNormalizationContext context
    )
    {
        var quantity = Math.Max(1, context.Request.Quantity);
        var teu = ResolveTeuFactor(context.Equipment);

        return NormalizeCharges(
            context,
            MarketRateBasis.PerContainer,
            charge => charge.SourceBasis switch
            {
                MarketRateBasis.PerContainer => ConversionResult.Exact(charge.AmountUsd),
                MarketRateBasis.PerShipment or MarketRateBasis.PerHbl =>
                    ConversionResult.Exact(charge.AmountUsd / quantity),
                MarketRateBasis.PerTeu when teu.HasValue =>
                    ConversionResult.Exact(charge.AmountUsd * teu.Value),
                MarketRateBasis.Unknown =>
                    ConversionResult.Assumed(
                        charge.AmountUsd,
                        0.70m,
                        $"Se asumió PerContainer para '{charge.Name}' porque el tarifario no indicó base."
                    ),
                MarketRateBasis.PerTeu =>
                    ConversionResult.Failure(
                        "missing_equipment_teu_factor",
                        $"No se pudo determinar el factor TEU del equipo para '{charge.Name}'."
                    ),
                _ =>
                    ConversionResult.Failure(
                        "unsupported_fcl_rate_basis",
                        $"La base {charge.SourceBasis} no es comparable como PerContainer en FCL."
                    ),
            }
        );
    }

    private static decimal? ResolveTeuFactor(MarketCatalogMatch equipment)
    {
        var canonical = MarketTextNormalizer.CanonicalEquipment(
            equipment.Code ?? equipment.Name ?? equipment.RawValue
        );

        if (canonical.StartsWith("20", StringComparison.Ordinal))
        {
            return 1m;
        }

        if (
            canonical.StartsWith("40", StringComparison.Ordinal)
            || canonical.StartsWith("45", StringComparison.Ordinal)
            || canonical.StartsWith("48", StringComparison.Ordinal)
            || canonical.StartsWith("53", StringComparison.Ordinal)
        )
        {
            return 2m;
        }

        return null;
    }
}

internal sealed class LclMarketRateNormalizationStrategy : MarketRateNormalizationStrategyBase
{
    public override bool CanHandle(ShipmentMode mode) => mode == ShipmentMode.Lcl;

    public override MarketStrategyNormalizationResult Normalize(
        MarketStrategyNormalizationContext context
    )
    {
        var wmUnits = ResolveWeightOrMeasureUnits(context.Request);

        return NormalizeCharges(
            context,
            MarketRateBasis.WeightOrMeasure,
            charge => charge.SourceBasis switch
            {
                MarketRateBasis.WeightOrMeasure
                    or MarketRateBasis.PerCbm
                    or MarketRateBasis.PerTon => ConversionResult.Exact(charge.AmountUsd),
                MarketRateBasis.PerKg => ConversionResult.Exact(charge.AmountUsd * 1000m),
                MarketRateBasis.Per100Kg => ConversionResult.Exact(charge.AmountUsd * 10m),
                MarketRateBasis.PerShipment or MarketRateBasis.PerHbl or MarketRateBasis.Minimum
                    when wmUnits.HasValue =>
                    ConversionResult.Exact(charge.AmountUsd / wmUnits.Value),
                MarketRateBasis.Unknown =>
                    ConversionResult.Assumed(
                        charge.AmountUsd,
                        0.65m,
                        $"Se asumió W/M para '{charge.Name}' porque el tarifario LCL no indicó base."
                    ),
                MarketRateBasis.PerShipment or MarketRateBasis.PerHbl or MarketRateBasis.Minimum =>
                    ConversionResult.Failure(
                        "missing_lcl_wm_units",
                        $"Se requiere CBM o peso para convertir '{charge.Name}' a W/M."
                    ),
                _ =>
                    ConversionResult.Failure(
                        "unsupported_lcl_rate_basis",
                        $"La base {charge.SourceBasis} no es comparable como W/M en LCL."
                    ),
            }
        );
    }
}

internal sealed class AirMarketRateNormalizationStrategy : MarketRateNormalizationStrategyBase
{
    public override bool CanHandle(ShipmentMode mode) =>
        mode is ShipmentMode.Air or ShipmentMode.AirConsol;

    public override MarketStrategyNormalizationResult Normalize(
        MarketStrategyNormalizationContext context
    )
    {
        var weightKg = ResolveAirWeightKg(context.Request);

        return NormalizeCharges(
            context,
            MarketRateBasis.PerKg,
            charge => charge.SourceBasis switch
            {
                MarketRateBasis.PerKg => ConversionResult.Exact(charge.AmountUsd),
                MarketRateBasis.Per100Kg => ConversionResult.Exact(charge.AmountUsd / 100m),
                MarketRateBasis.PerTon => ConversionResult.Exact(charge.AmountUsd / 1000m),
                MarketRateBasis.PerShipment or MarketRateBasis.PerHbl or MarketRateBasis.Minimum
                    when weightKg.HasValue =>
                    ConversionResult.Exact(charge.AmountUsd / weightKg.Value),
                MarketRateBasis.Unknown =>
                    ConversionResult.Assumed(
                        charge.AmountUsd,
                        0.65m,
                        $"Se asumió PerKg para '{charge.Name}' porque el tarifario aéreo no indicó base."
                    ),
                MarketRateBasis.PerShipment or MarketRateBasis.PerHbl or MarketRateBasis.Minimum =>
                    ConversionResult.Failure(
                        "missing_air_chargeable_weight",
                        $"Se requiere peso cobrable para convertir '{charge.Name}' a PerKg."
                    ),
                _ =>
                    ConversionResult.Failure(
                        "unsupported_air_rate_basis",
                        $"La base {charge.SourceBasis} no es comparable como PerKg en aéreo."
                    ),
            }
        );
    }
}

internal sealed class FtlMarketRateNormalizationStrategy : MarketRateNormalizationStrategyBase
{
    public override bool CanHandle(ShipmentMode mode) => mode == ShipmentMode.Ftl;

    public override MarketStrategyNormalizationResult Normalize(
        MarketStrategyNormalizationContext context
    )
    {
        var quantity = Math.Max(1, context.Request.Quantity);

        return NormalizeCharges(
            context,
            MarketRateBasis.PerTruck,
            charge => charge.SourceBasis switch
            {
                MarketRateBasis.PerTruck or MarketRateBasis.PerContainer =>
                    ConversionResult.Exact(charge.AmountUsd),
                MarketRateBasis.PerShipment or MarketRateBasis.PerHbl =>
                    ConversionResult.Exact(charge.AmountUsd / quantity),
                MarketRateBasis.Unknown =>
                    ConversionResult.Assumed(
                        charge.AmountUsd,
                        0.70m,
                        $"Se asumió PerTruck para '{charge.Name}' porque el tarifario FTL no indicó base."
                    ),
                _ =>
                    ConversionResult.Failure(
                        "unsupported_ftl_rate_basis",
                        $"La base {charge.SourceBasis} no es comparable como PerTruck en FTL."
                    ),
            }
        );
    }
}

internal sealed class LtlMarketRateNormalizationStrategy : MarketRateNormalizationStrategyBase
{
    public override bool CanHandle(ShipmentMode mode) => mode == ShipmentMode.Ltl;

    public override MarketStrategyNormalizationResult Normalize(
        MarketStrategyNormalizationContext context
    )
    {
        var weightKg = context.Request.WeightKg;
        var volumeCbm = context.Request.VolumeCbm;
        var wmUnits = ResolveWeightOrMeasureUnits(context.Request);

        return NormalizeCharges(
            context,
            MarketRateBasis.PerShipment,
            charge => charge.SourceBasis switch
            {
                MarketRateBasis.PerShipment or MarketRateBasis.PerHbl or MarketRateBasis.PerTruck =>
                    ConversionResult.Exact(charge.AmountUsd),
                MarketRateBasis.PerKg when weightKg is > 0m =>
                    ConversionResult.Exact(charge.AmountUsd * weightKg.Value),
                MarketRateBasis.Per100Kg when weightKg is > 0m =>
                    ConversionResult.Exact(charge.AmountUsd * (weightKg.Value / 100m)),
                MarketRateBasis.PerTon when weightKg is > 0m =>
                    ConversionResult.Exact(charge.AmountUsd * (weightKg.Value / 1000m)),
                MarketRateBasis.PerCbm when volumeCbm is > 0m =>
                    ConversionResult.Exact(charge.AmountUsd * volumeCbm.Value),
                MarketRateBasis.WeightOrMeasure when wmUnits.HasValue =>
                    ConversionResult.Exact(charge.AmountUsd * wmUnits.Value),
                MarketRateBasis.Minimum => ConversionResult.Exact(charge.AmountUsd),
                MarketRateBasis.Unknown =>
                    ConversionResult.Assumed(
                        charge.AmountUsd,
                        0.65m,
                        $"Se asumió PerShipment para '{charge.Name}' porque el tarifario LTL no indicó base."
                    ),
                MarketRateBasis.PerKg
                    or MarketRateBasis.Per100Kg
                    or MarketRateBasis.PerTon
                    or MarketRateBasis.PerCbm
                    or MarketRateBasis.WeightOrMeasure =>
                    ConversionResult.Failure(
                        "missing_ltl_measurement",
                        $"Falta peso o volumen para convertir '{charge.Name}' a PerShipment."
                    ),
                _ =>
                    ConversionResult.Failure(
                        "unsupported_ltl_rate_basis",
                        $"La base {charge.SourceBasis} no es comparable como PerShipment en LTL."
                    ),
            }
        );
    }
}
