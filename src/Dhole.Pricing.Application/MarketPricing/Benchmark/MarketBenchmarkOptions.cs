namespace Dhole.Pricing.Application.MarketPricing.Benchmark;

public sealed class MarketBenchmarkOptions
{
    public decimal MadModifiedZThreshold { get; set; } = 3.5m;
    public decimal IqrMultiplier { get; set; } = 1.5m;
    public int MinimumOutlierSampleSize { get; set; } = 3;

    public decimal LowDispersionCvThreshold { get; set; } = 0.05m;
    public decimal ModerateDispersionCvThreshold { get; set; } = 0.10m;
    public decimal HighDispersionCvThreshold { get; set; } = 0.20m;
    public decimal VeryHighDispersionCvThreshold { get; set; } = 0.35m;

    public decimal LowDispersionConfidenceFactor { get; set; } = 1.00m;
    public decimal ModerateDispersionConfidenceFactor { get; set; } = 0.95m;
    public decimal HighDispersionConfidenceFactor { get; set; } = 0.85m;
    public decimal VeryHighDispersionConfidenceFactor { get; set; } = 0.70m;
    public decimal ExtremeDispersionConfidenceFactor { get; set; } = 0.55m;

    public decimal MaximumOutlierConfidencePenalty { get; set; } = 0.35m;

    public void Validate()
    {
        if (MadModifiedZThreshold <= 0m)
            throw new InvalidOperationException("MadModifiedZThreshold debe ser mayor que cero.");

        if (IqrMultiplier <= 0m)
            throw new InvalidOperationException("IqrMultiplier debe ser mayor que cero.");

        if (MinimumOutlierSampleSize < 3)
            throw new InvalidOperationException("MinimumOutlierSampleSize debe ser al menos 3.");

        ValidateRatio(LowDispersionCvThreshold, nameof(LowDispersionCvThreshold));
        ValidateRatio(ModerateDispersionCvThreshold, nameof(ModerateDispersionCvThreshold));
        ValidateRatio(HighDispersionCvThreshold, nameof(HighDispersionCvThreshold));
        ValidateRatio(VeryHighDispersionCvThreshold, nameof(VeryHighDispersionCvThreshold));

        if (
            !(LowDispersionCvThreshold <= ModerateDispersionCvThreshold
                && ModerateDispersionCvThreshold <= HighDispersionCvThreshold
                && HighDispersionCvThreshold <= VeryHighDispersionCvThreshold)
        )
        {
            throw new InvalidOperationException(
                "Los umbrales de dispersión deben estar ordenados de menor a mayor."
            );
        }

        ValidateRatio(LowDispersionConfidenceFactor, nameof(LowDispersionConfidenceFactor));
        ValidateRatio(ModerateDispersionConfidenceFactor, nameof(ModerateDispersionConfidenceFactor));
        ValidateRatio(HighDispersionConfidenceFactor, nameof(HighDispersionConfidenceFactor));
        ValidateRatio(VeryHighDispersionConfidenceFactor, nameof(VeryHighDispersionConfidenceFactor));
        ValidateRatio(ExtremeDispersionConfidenceFactor, nameof(ExtremeDispersionConfidenceFactor));
        ValidateRatio(MaximumOutlierConfidencePenalty, nameof(MaximumOutlierConfidencePenalty));
    }

    private static void ValidateRatio(decimal value, string name)
    {
        if (value < 0m || value > 1m)
            throw new InvalidOperationException($"{name} debe estar entre 0 y 1.");
    }
}
