namespace Dhole.Pricing.Application.MarketPricing.Comparability;

public sealed class MarketComparabilityOptions
{
    public const string SectionName = "Pricing:MarketComparability";

    public decimal IncotermWeight { get; set; } = 0.20m;
    public decimal RouteWeight { get; set; } = 0.25m;
    public decimal EquipmentWeight { get; set; } = 0.20m;
    public decimal ModeWeight { get; set; } = 0.15m;
    public decimal CarrierWeight { get; set; } = 0.08m;
    public decimal RecencyWeight { get; set; } = 0.05m;
    public decimal ExtractionWeight { get; set; } = 0.03m;
    public decimal NormalizationWeight { get; set; } = 0.02m;
    public decimal CompletenessWeight { get; set; } = 0.02m;

    public double RecencyDecayDays { get; set; } = 60d;
    public decimal ExpiredRecencyPenalty { get; set; } = 0.65m;
    public int HistoricalLookbackDays { get; set; } = 365;
    public int MinimumPrimaryCarrierObservations { get; set; } = 2;
    public int MinimumCompetitorsForSufficientMarket { get; set; } = 2;
    public int MinimumObservationsForSufficientMarket { get; set; } = 2;

    public decimal SecondaryCarrierScore { get; set; } = 0.45m;
    public decimal MissingCarrierScore { get; set; } = 0.25m;
    public decimal MissingRouteDimensionScore { get; set; } = 0.60m;
    public decimal MoreSpecificRouteDimensionScore { get; set; } = 0.85m;

    public decimal HighConfidenceThreshold { get; set; } = 80m;
    public decimal MediumConfidenceThreshold { get; set; } = 60m;

    public void Validate()
    {
        var total =
            IncotermWeight
            + RouteWeight
            + EquipmentWeight
            + ModeWeight
            + CarrierWeight
            + RecencyWeight
            + ExtractionWeight
            + NormalizationWeight
            + CompletenessWeight;

        if (Math.Abs(total - 1m) > 0.000001m)
        {
            throw new InvalidOperationException(
                $"Los pesos de comparabilidad deben sumar 1.00. Valor actual: {total:0.######}."
            );
        }

        if (RecencyDecayDays <= 0d)
            throw new InvalidOperationException("RecencyDecayDays debe ser mayor que cero.");

        if (HistoricalLookbackDays < 0)
            throw new InvalidOperationException("HistoricalLookbackDays no puede ser negativo.");

        ValidateRatio(ExpiredRecencyPenalty, nameof(ExpiredRecencyPenalty));
        ValidateRatio(SecondaryCarrierScore, nameof(SecondaryCarrierScore));
        ValidateRatio(MissingCarrierScore, nameof(MissingCarrierScore));
        ValidateRatio(MissingRouteDimensionScore, nameof(MissingRouteDimensionScore));
        ValidateRatio(MoreSpecificRouteDimensionScore, nameof(MoreSpecificRouteDimensionScore));
    }

    private static void ValidateRatio(decimal value, string name)
    {
        if (value < 0m || value > 1m)
            throw new InvalidOperationException($"{name} debe estar entre 0 y 1.");
    }
}
