using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Shared;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Dhole.Pricing.Application.Services;

public sealed class RateExtraDetailResolver(
    ICostRepository costs,
    IPricingConfigCatalogClient configCatalog
) : IRateExtraDetailResolver
{
    public async Task<RateExtraDetailResolution> ResolveAsync(
        RateExtraDetailInput input,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return RateExtraDetailResolution.Failure(PricingErrors.RateCostDetailNameIsRequired);
        }

        if (input.CostAmount < 0m || input.SaleAmount < 0m)
        {
            return RateExtraDetailResolution.Failure(
                PricingErrors.RateCostDetailAmountMustBeGreaterOrEqualThanZero
            );
        }

        if (input.Quantity is < 0)
        {
            return RateExtraDetailResolution.Failure(PricingErrors.RateInvalidDetailQuantity);
        }

        if (input.CurrencyId == Guid.Empty)
        {
            return RateExtraDetailResolution.Failure(
                PricingErrors.RateCostDetailCurrencyIsRequired
            );
        }

        PricingConfigCatalogItem? currency;
        try
        {
            currency = await configCatalog.GetActiveInGroupAsync(
                input.CurrencyId,
                PricingConstants.CatalogSlugs.Currencies,
                cancellationToken
            );
        }
        catch (InvalidOperationException)
        {
            return RateExtraDetailResolution.Failure(PricingErrors.ConfigServiceUnavailable);
        }

        if (currency is null)
        {
            return RateExtraDetailResolution.Failure(
                PricingErrors.InvalidConfigCatalogReference(
                    "moneda del detalle",
                    PricingConstants.CatalogSlugs.Currencies
                )
            );
        }

        // CostId puede ser null por dos motivos válidos: un cargo manual o un cargo/recargo
        // externo que viene de una matriz LCL/coloader y no existe en el maestro Costs.
        // Esos snapshots externos pueden ser Fixed (Manejos, HBL, etc.) y deben persistirse
        // tal como fueron calculados para que la tarifa guardada coincida con el PDF.
        if (!input.CostId.HasValue)
        {
            var (costAmount, saleAmount) = ResolveGeneratedInsuranceAmounts(input);

            return RateExtraDetailResolution.Success(
                new ResolvedRateExtraDetail(
                    input.Id,
                    CostId: null,
                    input.Name.Trim(),
                    input.CostDetailType,
                    input.CostType,
                    currency.Id,
                    currency.SnapshotName(),
                    currency.Code,
                    costAmount,
                    saleAmount,
                    Normalize(input.Notes),
                    IsAccountant: false,
                    input.Quantity,
                    input.ChargeBasis,
                    input.ApplyDestinationTax,
                    input.DestinationTaxRate
                )
            );
        }

        var cost = await costs.GetByIdAsync(input.CostId.Value, cancellationToken);

        if (cost is null)
        {
            return RateExtraDetailResolution.Failure(PricingErrors.CostNotFound);
        }

        if (cost.CostType == CostType.Fixed)
        {
            if (!input.Id.HasValue && (cost.IsDeleted || !cost.IsActive))
            {
                return RateExtraDetailResolution.Failure(
                    cost.IsDeleted ? PricingErrors.CostNotFound : PricingErrors.CostIsInactive
                );
            }

            var selectedCurrencyDiffers = currency.Id != cost.CurrencyId;
            var costAmount = input.Id.HasValue || selectedCurrencyDiffers
                ? input.CostAmount
                : cost.CostAmount;
            var saleAmount = cost.AgentId.HasValue ? 0m : input.SaleAmount;

            return RateExtraDetailResolution.Success(
                new ResolvedRateExtraDetail(
                    input.Id,
                    cost.Id,
                    cost.Name,
                    cost.CostDetailType,
                    cost.CostType,
                    currency.Id,
                    currency.SnapshotName(),
                    currency.Code,
                    costAmount,
                    saleAmount,
                    Normalize(input.Notes) ?? cost.Notes,
                    cost.IsAccountant,
                    input.Quantity,
                    cost.ChargeBasis,
                    input.ApplyDestinationTax,
                    input.DestinationTaxRate
                )
            );
        }

        if (cost.IsDeleted)
        {
            return RateExtraDetailResolution.Failure(PricingErrors.CostNotFound);
        }

        if (!cost.IsActive)
        {
            return RateExtraDetailResolution.Failure(PricingErrors.CostIsInactive);
        }

        return RateExtraDetailResolution.Success(
            new ResolvedRateExtraDetail(
                input.Id,
                cost.Id,
                cost.Name,
                cost.CostDetailType,
                cost.CostType,
                currency.Id,
                currency.SnapshotName(),
                currency.Code,
                input.CostAmount,
                input.SaleAmount,
                Normalize(input.Notes) ?? cost.Notes,
                cost.IsAccountant,
                input.Quantity,
                cost.ChargeBasis,
                input.ApplyDestinationTax,
                input.DestinationTaxRate
            )
        );
    }

    private static (decimal CostAmount, decimal SaleAmount) ResolveGeneratedInsuranceAmounts(
        RateExtraDetailInput input
    )
    {
        if (
            input.CostDetailType != CostDetailType.Insurance
            || string.IsNullOrWhiteSpace(input.Notes)
            || !(
                input.Notes.StartsWith(
                    "Seguro de carga · valor FOB USD",
                    StringComparison.OrdinalIgnoreCase
                )
                || input.Notes.StartsWith(
                    "Seguro de carga · valor carga USD",
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
        {
            return (input.CostAmount, input.SaleAmount);
        }

        var cargoValue =
            ReadDecimal(input.Notes, @"valor FOB USD\s+([0-9]+(?:[.,][0-9]+)?)")
            ?? ReadDecimal(input.Notes, @"valor carga USD\s+([0-9]+(?:[.,][0-9]+)?)");
        if (!cargoValue.HasValue || cargoValue.Value <= 0m)
        {
            return (input.CostAmount, input.SaleAmount);
        }

        var freightAmount =
            ReadDecimal(input.Notes, @"flete USD\s+([0-9]+(?:[.,][0-9]+)?)") ?? 0m;
        var salePercentage =
            ReadDecimal(input.Notes, @"tasa\s+([0-9]+(?:[.,][0-9]+)?)%")
            ?? ReadDecimal(input.Notes, @"·\s*([0-9]+(?:[.,][0-9]+)?)%")
            ?? CargoInsurancePricingRules.DefaultSalePercentage;
        var saleMinimum =
            ReadDecimal(input.Notes, @"mínimo USD\s+([0-9]+(?:[.,][0-9]+)?)")
            ?? CargoInsurancePricingRules.DefaultSaleMinimumAmount;
        decimal? manualSaleAmount = input.Notes.Contains(
            "tarifa manual",
            StringComparison.OrdinalIgnoreCase
        )
            ? input.SaleAmount
            : null;

        var calculated = CargoInsurancePricingRules.Calculate(
            cargoValue.Value,
            freightAmount,
            salePercentage,
            saleMinimum,
            manualSaleAmount
        );

        return (calculated.CostAmount, calculated.SaleAmount);
    }

    private static decimal? ReadDecimal(string source, string pattern)
    {
        var match = Regex.Match(
            source,
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
        if (!match.Success || match.Groups.Count < 2) return null;

        var raw = match.Groups[1].Value.Replace(',', '.');
        return decimal.TryParse(
            raw,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var value
        )
            ? value
            : null;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
