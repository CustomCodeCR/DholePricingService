using System.Globalization;
using System.Text;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Costs.Entities;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
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
        var existingAmounts = rate
            .RateDetails.Where(x => x.CostId.HasValue && x.CostType == CostType.Fixed)
            .GroupBy(x => x.CostId!.Value)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var detail = group.First();
                    return (detail.CostAmount, detail.SaleAmount, detail.Quantity);
                }
            );

        rate.RemoveAutomaticFixedDetails(updatedBy);

        var activeFixedCosts = await costs.GetActiveCostsAsync(
            costType: CostType.Fixed,
            cancellationToken: cancellationToken
        );

        foreach (var cost in activeFixedCosts.Where(cost => MatchesRate(cost, rate)))
        {
            var hasExistingAmount = existingAmounts.TryGetValue(cost.Id, out var existingAmount);
            var costAmount = hasExistingAmount ? existingAmount.CostAmount : cost.CostAmount;
            var saleAmount = cost.AgentId.HasValue
                ? 0m
                : hasExistingAmount
                    ? existingAmount.SaleAmount
                    : cost.SaleAmount;

            rate.AddRateDetail(
                rate.Id,
                cost.Id,
                cost.Name,
                cost.CostDetailType,
                cost.CostType,
                cost.CurrencyId,
                cost.CurrencyName,
                cost.CurrencyCode,
                costAmount,
                saleAmount,
                cost.Notes,
                cost.IsAccountant ? rate.ContainerQuantity : 1,
                updatedBy
            );
        }

        await AddPanamaGamInternationalLandFreightAsync(rate, updatedBy, cancellationToken);
    }

    private async Task AddPanamaGamInternationalLandFreightAsync(
        RateHeader rate,
        Guid? updatedBy,
        CancellationToken cancellationToken
    )
    {
        if (!IsPanamaToGam(rate))
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

    private static bool MatchesRate(Cost cost, RateHeader rate)
    {
        var matchesAgent = !cost.AgentId.HasValue || cost.AgentId == rate.AgentId;
        var matchesCarrier = !cost.CarrierId.HasValue || cost.CarrierId == rate.CarrierId;
        var matchesIncoterm =
            cost.Incoterms.Count == 0
            || (
                rate.IncotermId.HasValue
                && cost.Incoterms.Any(x => x.IncotermId == rate.IncotermId.Value)
            );

        if (!matchesAgent || !matchesCarrier || !matchesIncoterm)
            return false;

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
