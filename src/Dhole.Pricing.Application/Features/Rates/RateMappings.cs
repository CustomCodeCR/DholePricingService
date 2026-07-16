using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.Application.Features.Rates;

internal static class RateMappings
{
    public static RateDto ToDto(this RateHeader rate)
    {
        return new RateDto(
            rate.Id,
            rate.RateCode,
            rate.RateName,
            rate.SourceImportFclRateId,
            rate.AgentId,
            rate.AgentName,
            rate.AgentCode,
            rate.CarrierId,
            rate.CarrierName,
            rate.CarrierCode,
            rate.PolId,
            rate.PolName,
            rate.PolCode,
            rate.PoeId,
            rate.PoeName,
            rate.PoeCode,
            rate.PodId,
            rate.PodName,
            rate.PodCode,
            rate.ContainerTypeId,
            rate.ContainerTypeName,
            rate.ContainerTypeCode,
            rate.ContainerQuantity,
            rate.CurrencyId,
            rate.CurrencyName,
            rate.CurrencyCode,
            rate.FreeDays,
            rate.ValidFrom,
            rate.ValidTo,
            rate.TotalCostAmount,
            rate.TotalSaleAmount,
            rate.TotalUtilityAmount,
            rate.MarginPercentage,
            rate.RequiredApproval,
            rate.Status.ToString(),
            rate.RateDetails.OrderBy(x => x.CostDetailType)
                .ThenBy(x => x.Name)
                .Select(x => new RateDetailDto(
                    x.Id,
                    x.RateHeaderId,
                    x.CostId,
                    x.Name,
                    x.CostDetailType.ToString(),
                    x.CostType.ToString(),
                    x.CurrencyId,
                    x.CurrencyName,
                    x.CurrencyCode,
                    x.CostAmount,
                    x.SaleAmount,
                    x.UtilityAmount,
                    x.Quantity,
                    x.Notes
                ))
                .ToList()
        );
    }
}
