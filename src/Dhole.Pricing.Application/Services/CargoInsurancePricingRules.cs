namespace Dhole.Pricing.Application.Services;

public static class CargoInsurancePricingRules
{
    // Manual Pricing CRC: (Valor FOB + Flete) x 110% x 0.85%, mínimo USD 125.
    public const decimal InsuredValueFactor = 1.10m;
    public const decimal DefaultSalePercentage = 0.85m;
    public const decimal DefaultSaleMinimumAmount = 125m;

    // El manual comercial no publica el costo interno. Se conserva la regla contable existente,
    // pero sobre la misma base asegurada para mantener coherencia del margen.
    public const decimal CostPercentage = 0.20m;
    public const decimal CostMinimumAmount = 35m;

    public static CargoInsurancePricingResult Calculate(
        decimal cargoValue,
        decimal freightAmount = 0m,
        decimal salePercentage = DefaultSalePercentage,
        decimal saleMinimumAmount = DefaultSaleMinimumAmount,
        decimal? manualSaleAmount = null
    )
    {
        var baseValue = Math.Max(0m, cargoValue) + Math.Max(0m, freightAmount);
        if (baseValue <= 0m)
            return new CargoInsurancePricingResult(0m, 0m);

        var insuredValue = baseValue * InsuredValueFactor;
        var normalizedSalePercentage = Math.Max(0m, salePercentage);
        var normalizedSaleMinimum = Math.Max(0m, saleMinimumAmount);

        var costAmount = Math.Max(
            CostMinimumAmount,
            RoundCurrency(insuredValue * (CostPercentage / 100m))
        );

        var calculatedSaleAmount = Math.Max(
            normalizedSaleMinimum,
            RoundCurrency(insuredValue * (normalizedSalePercentage / 100m))
        );

        var saleAmount = manualSaleAmount.HasValue
            ? Math.Max(0m, RoundCurrency(manualSaleAmount.Value))
            : calculatedSaleAmount;

        return new CargoInsurancePricingResult(costAmount, saleAmount);
    }

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed record CargoInsurancePricingResult(decimal CostAmount, decimal SaleAmount);
