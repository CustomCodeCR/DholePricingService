namespace Dhole.Pricing.Application.Services;

public static class CargoInsurancePricingRules
{
    public const decimal DefaultSalePercentage = 0.65m;
    public const decimal DefaultSaleMinimumAmount = 95m;
    public const decimal CostPercentage = 0.20m;
    public const decimal CostMinimumAmount = 35m;

    public static CargoInsurancePricingResult Calculate(
        decimal cargoValue,
        decimal salePercentage = DefaultSalePercentage,
        decimal saleMinimumAmount = DefaultSaleMinimumAmount,
        decimal? manualSaleAmount = null
    )
    {
        if (cargoValue <= 0m)
        {
            return new CargoInsurancePricingResult(0m, 0m);
        }

        var normalizedSalePercentage = Math.Max(0m, salePercentage);
        var normalizedSaleMinimum = Math.Max(0m, saleMinimumAmount);

        var costAmount = Math.Max(
            CostMinimumAmount,
            RoundCurrency(cargoValue * (CostPercentage / 100m))
        );

        var calculatedSaleAmount = Math.Max(
            normalizedSaleMinimum,
            RoundCurrency(cargoValue * (normalizedSalePercentage / 100m))
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
