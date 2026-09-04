using System.Globalization;
using System.Text;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Costs.Entities;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Services;

public sealed class RateFixedCostSynchronizer(
    ICostRepository costs,
    IPricingConfigCatalogClient configCatalog
) : IRateFixedCostSynchronizer
{
    private const decimal PanamaGamInternationalLandFreight = 2140m;

    // No existe FK desde RateDetail.CostId hacia Costs. Este identificador estable permite
    // tratar la regla de ruta como un costo fijo automático y resincronizarla sin duplicados.
    private static readonly Guid PanamaGamLandFreightRuleId =
        Guid.Parse("A2140000-0000-4000-8000-000000000001");

    public async Task SynchronizeAsync(
        RateHeader rate,
        Guid? updatedBy,
        CancellationToken cancellationToken = default
    )
    {
        // LCL propio ya lleva la matriz del consolidado prorrateada dentro del flete/CBM.
        // En la tarifa comercial solo deben persistir el flete y las líneas adicionales
        // provenientes de las reglas/Excel, que no están ligadas a CostId.
        if (IsOwnLclRate(rate))
        {
            var configuredDetailIds = rate.RateDetails
                .Where(x => x.CostId.HasValue)
                .Select(x => x.Id)
                .ToArray();

            foreach (var detailId in configuredDetailIds)
            {
                rate.RemoveRateDetail(detailId, updatedBy);
            }

            return;
        }

        var existingAmounts = rate
            .RateDetails.Where(x => x.CostId.HasValue && x.CostType == CostType.Fixed)
            .GroupBy(x => x.CostId!.Value)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var detail = group.First();
                    return (
                        detail.CostAmount,
                        detail.SaleAmount,
                        detail.Quantity,
                        detail.CurrencyId,
                        detail.CurrencyName,
                        detail.CurrencyCode,
                        detail.ApplyDestinationTax,
                        detail.DestinationTaxRate
                    );
                }
            );

        rate.RemoveAutomaticFixedDetails(updatedBy);

        var activeFixedCosts = await costs.GetActiveCostsAsync(
            costType: CostType.Fixed,
            cancellationToken: cancellationToken
        );

        var hasExplicitFreight = rate.RateDetails.Any(x =>
            x.CostDetailType == CostDetailType.Freight && !x.CostId.HasValue
        );
        var exchangeRateSale = rate.ExchangeRateApplied is > 0m
            ? rate.ExchangeRateApplied.Value
            : rate.ExchangeRateSale is > 0m
                ? rate.ExchangeRateSale.Value
                : 0m;
        PricingConfigCatalogItem? crcCurrency = null;

        foreach (var cost in activeFixedCosts.Where(cost =>
            MatchesRate(cost, rate)
            && !(hasExplicitFreight && cost.CostDetailType == CostDetailType.Freight)
        ))
        {
            var hasExistingAmount = existingAmounts.TryGetValue(cost.Id, out var existingAmount);
            var hasMinimumRule = cost.MinimumCostAmount.HasValue || cost.MinimumSaleAmount.HasValue;
            var forceCrc = CostaRicaServiceCurrencyRules.RequiresCrc(cost, rate);

            Guid targetCurrencyId;
            string targetCurrencyName;
            string targetCurrencyCode;

            if (forceCrc)
            {
                crcCurrency ??= await configCatalog.GetActiveByCodeAsync(
                    PricingConstants.CatalogSlugs.Currencies,
                    "CRC",
                    cancellationToken
                );
                if (crcCurrency is null)
                    throw new InvalidOperationException(
                        "No se encontró la moneda CRC activa en el catálogo 'currencies' de Config."
                    );

                targetCurrencyId = crcCurrency.Id;
                targetCurrencyName = crcCurrency.SnapshotName();
                targetCurrencyCode = crcCurrency.Code;
            }
            else if (hasExistingAmount)
            {
                // Si Pricing escogió una moneda por línea, conservarla al resincronizar el fijo.
                targetCurrencyId = existingAmount.CurrencyId;
                targetCurrencyName = existingAmount.CurrencyName;
                targetCurrencyCode = existingAmount.CurrencyCode;
            }
            else
            {
                targetCurrencyId = cost.CurrencyId;
                targetCurrencyName = cost.CurrencyName;
                targetCurrencyCode = cost.CurrencyCode;
            }

            // El costo contable de un fijo siempre parte del maestro y se convierte a la moneda
            // de la línea. La venta puede conservar el override de Pricing, convirtiéndolo si hace falta.
            var costAmount = CostaRicaServiceCurrencyRules.ConvertUsdCrc(
                cost.CostAmount, cost.CurrencyCode, targetCurrencyCode, exchangeRateSale);
            var saleAmount = cost.AgentId.HasValue
                ? 0m
                : hasExistingAmount && !hasMinimumRule
                    ? CostaRicaServiceCurrencyRules.ConvertUsdCrc(
                        existingAmount.SaleAmount,
                        existingAmount.CurrencyCode,
                        targetCurrencyCode,
                        exchangeRateSale)
                    : CostaRicaServiceCurrencyRules.ConvertUsdCrc(
                        cost.SaleAmount, cost.CurrencyCode, targetCurrencyCode, exchangeRateSale);

            var minimumCostAmount = cost.MinimumCostAmount.HasValue
                ? CostaRicaServiceCurrencyRules.ConvertUsdCrc(
                    cost.MinimumCostAmount.Value, cost.CurrencyCode, targetCurrencyCode, exchangeRateSale)
                : 0m;
            var minimumSaleAmount = cost.MinimumSaleAmount.HasValue
                ? CostaRicaServiceCurrencyRules.ConvertUsdCrc(
                    cost.MinimumSaleAmount.Value, cost.CurrencyCode, targetCurrencyCode, exchangeRateSale)
                : 0m;

            var quantity = rate.ResolveChargeQuantity(cost.ChargeBasis, kgPerCbmOverride: cost.KgPerCbm);
            var effectiveCostTotal = Math.Max(costAmount * quantity, minimumCostAmount);
            var effectiveSaleTotal = cost.AgentId.HasValue
                ? 0m
                : Math.Max(saleAmount * quantity, minimumSaleAmount);
            var effectiveCostAmount = quantity > 0m ? effectiveCostTotal / quantity : effectiveCostTotal;
            var effectiveSaleAmount = quantity > 0m ? effectiveSaleTotal / quantity : effectiveSaleTotal;

            var synchronizedDetail = rate.AddRateDetail(
                rate.Id,
                cost.Id,
                cost.Name,
                cost.CostDetailType,
                cost.CostType,
                cost.ChargeBasis,
                targetCurrencyId,
                targetCurrencyName,
                targetCurrencyCode,
                effectiveCostAmount,
                effectiveSaleAmount,
                cost.Notes,
                quantity,
                updatedBy
            );

            if (hasExistingAmount)
            {
                synchronizedDetail.ConfigureDestinationTax(
                    existingAmount.ApplyDestinationTax,
                    existingAmount.DestinationTaxRate
                );
            }
        }

        await AddPanamaGamInternationalLandFreightAsync(rate, updatedBy, cancellationToken);
    }

    private async Task AddPanamaGamInternationalLandFreightAsync(
        RateHeader rate,
        Guid? updatedBy,
        CancellationToken cancellationToken
    )
    {
        if (rate.ShipmentMode != ShipmentMode.Fcl || !IsPanamaToGam(rate))
            return;

        Guid currencyId;
        string currencyName;
        string currencyCode;

        if (string.Equals(rate.CurrencyCode, "USD", StringComparison.OrdinalIgnoreCase))
        {
            currencyId = rate.CurrencyId;
            currencyName = rate.CurrencyName;
            currencyCode = rate.CurrencyCode;
        }
        else
        {
            var usd = await configCatalog.GetActiveByCodeAsync(
                PricingConstants.CatalogSlugs.Currencies,
                "USD",
                cancellationToken
            );

            if (usd is null)
                throw new InvalidOperationException(
                    "No se encontró la moneda USD activa en el catálogo 'currencies' de Config."
                );

            currencyId = usd.Id;
            currencyName = usd.Name;
            currencyCode = usd.Code;
        }

        rate.AddRateDetail(
            rate.Id,
            PanamaGamLandFreightRuleId,
            "Flete internacional terrestre",
            CostDetailType.InlandTransport,
            CostType.Fixed,
            ChargeBasis.PerContainer,
            currencyId,
            currencyName,
            currencyCode,
            PanamaGamInternationalLandFreight,
            PanamaGamInternationalLandFreight,
            "Aplicado automáticamente para la ruta POE Panamá → POD GAM.",
            rate.ContainerQuantity,
            updatedBy
        );
    }

    private static bool IsOwnLclRate(RateHeader rate)
    {
        if (rate.ShipmentMode != ShipmentMode.Lcl)
            return false;

        var headerMarker = NormalizeRouteText(rate.RateName);
        if (headerMarker.Contains("LCL PROPIO", StringComparison.Ordinal)
            || headerMarker.Contains("CONSOLIDADO PROPIO", StringComparison.Ordinal))
            return true;

        return rate.RateDetails.Any(detail =>
        {
            if (detail.CostId.HasValue)
                return false;

            var marker = NormalizeRouteText($"{detail.Name} {detail.Notes}");
            if (marker.Contains("LCL PROPIO", StringComparison.Ordinal)
                || marker.Contains("CONSOLIDADO PROPIO", StringComparison.Ordinal))
                return true;

            // Las líneas calculadas por OwnLclConsolidationEndpoints conservan esta nota
            // para bases HBL/SET. Es la señal de que la tarifa viene exclusivamente de la
            // matriz Excel del consolidado propio y no debe mezclarse con Costos y recargos.
            return detail.CostType == CostType.Variable
                && marker.Contains("BASE DEL EXCEL", StringComparison.Ordinal);
        });
    }

    private static bool MatchesRate(Cost cost, RateHeader rate)
    {
        var matchesAgent = !cost.AgentId.HasValue || cost.AgentId == rate.AgentId;
        var matchesCarrier = !cost.CarrierId.HasValue || cost.CarrierId == rate.CarrierId;
        var matchesMode = !cost.ShipmentMode.HasValue || cost.ShipmentMode.Value == rate.ShipmentMode;
        var matchesIncoterm =
            cost.Incoterms.Count == 0
            || (
                rate.IncotermId.HasValue
                && cost.Incoterms.Any(x => x.IncotermId == rate.IncotermId.Value)
            );
        var matchesServices =
            cost.Services.Count == 0
            || cost.Services.Any(costService =>
                rate.RateServices.Any(rateService => rateService.ServiceId == costService.ServiceId));

        if (!matchesAgent || !matchesCarrier || !matchesMode || !matchesIncoterm || !matchesServices)
            return false;

        var hasStructuredRoute = cost.PolId.HasValue || cost.PoeId.HasValue || cost.PodId.HasValue;
        if (hasStructuredRoute)
        {
            if (cost.PolId.HasValue && cost.PolId != rate.PolId)
                return false;
            if (cost.PoeId.HasValue && cost.PoeId != rate.PoeId)
                return false;
            if (cost.PodId.HasValue && cost.PodId != rate.PodId)
                return false;

            return true;
        }

        if (!cost.PortId.HasValue)
            return true;

        return cost.PortRole switch
        {
            CostPortRole.Pol => cost.PortId == rate.PolId,
            CostPortRole.Poe => cost.PortId == rate.PoeId,
            CostPortRole.Pod => cost.PortId == rate.PodId,
            CostPortRole.Any =>
                cost.PortId == rate.PolId || cost.PortId == rate.PoeId || cost.PortId == rate.PodId,
            null => cost.PortId == rate.PolId || cost.PortId == rate.PoeId || cost.PortId == rate.PodId,
            _ => false,
        };
    }

    private static bool IsPanamaToGam(RateHeader rate)
    {
        var poe = NormalizeRouteText($"{rate.PoeCode} {rate.PoeName}");
        var pod = NormalizeRouteText($"{rate.PodCode} {rate.PodName}");

        var isPanamaPoe = new[]
        {
            "PANAMA",
            "COLON",
            "MANZANILLO",
            "RODMAN",
            "CRISTOBAL",
            "BALBOA",
        }.Any(poe.Contains);

        var podTokens = pod.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var isGamPod = podTokens.Contains("GAM", StringComparer.Ordinal)
            || pod.Contains("GRAN AREA METROPOLITANA", StringComparison.Ordinal);

        return isPanamaPoe && isGamPod;
    }

    private static string NormalizeRouteText(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : ' ');
        }

        return string.Join(
            ' ',
            builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
        );
    }
}
