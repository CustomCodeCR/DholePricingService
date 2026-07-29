using Dhole.Pricing.Domain.Costs.Entities;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.Application.Auditing;

public static class PricingAuditSnapshots
{
    public static object From(Cost cost) =>
        new
        {
            cost.Id,
            cost.Name,

            CostType = cost.CostType.ToString(),
            CostDetailType = cost.CostDetailType.ToString(),

            cost.CarrierId,
            cost.CarrierName,
            cost.CarrierCode,

            cost.AgentId,
            cost.AgentName,
            cost.AgentCode,

            cost.PortId,
            cost.PortName,
            cost.PortCode,
            PortRole = cost.PortRole.ToString(),

            cost.CurrencyId,
            cost.CurrencyName,
            cost.CurrencyCode,

            cost.CostAmount,
            cost.SaleAmount,
            cost.UtilityAmount,

            cost.Notes,
            cost.IsAccountant,
            cost.IsActive,
        };

    public static object From(ImportFclRates importFclRate) =>
        new
        {
            importFclRate.Id,
            importFclRate.ImportBatchId,

            SourceType = importFclRate.SourceType.ToString(),

            importFclRate.Pol,
            importFclRate.Pod,
            importFclRate.Carrier,
            importFclRate.ContainerType,
            importFclRate.Currency,

            importFclRate.Freight,
            importFclRate.FreeDays,
            importFclRate.ValidFrom,
            importFclRate.ValidTo,

            Status = importFclRate.Status.ToString(),

            importFclRate.RawDataJson,
            importFclRate.SourceUrl,
            importFclRate.UsedAsRateCount,
            importFclRate.CreatedAsRateHeaderId,
        };

    public static object From(RateHeader rateHeader) =>
        new
        {
            rateHeader.Id,
            rateHeader.RateCode,
            rateHeader.RateName,

            rateHeader.SourceImportFclRateId,

            rateHeader.AgentId,
            rateHeader.AgentName,
            rateHeader.AgentCode,

            rateHeader.CarrierId,
            rateHeader.CarrierName,
            rateHeader.CarrierCode,

            rateHeader.PolId,
            rateHeader.PolName,
            rateHeader.PolCode,

            rateHeader.PoeId,
            rateHeader.PoeName,
            rateHeader.PoeCode,

            rateHeader.PodId,
            rateHeader.PodName,
            rateHeader.PodCode,

            rateHeader.ContainerTypeId,
            rateHeader.ContainerTypeName,
            rateHeader.ContainerTypeCode,
            rateHeader.ContainerQuantity,

            rateHeader.CurrencyId,
            rateHeader.CurrencyName,
            rateHeader.CurrencyCode,

            rateHeader.FreeDays,
            rateHeader.ValidFrom,
            rateHeader.ValidTo,
            rateHeader.ClientName,
            rateHeader.IdtraNumber,
            rateHeader.QuoNumber,
            rateHeader.Includes,
            rateHeader.SubjectTo,
            rateHeader.Excludes,
            rateHeader.TransitDays,

            rateHeader.TotalCostAmount,
            rateHeader.TotalSaleAmount,
            rateHeader.TotalUtilityAmount,
            rateHeader.MarginPercentage,
            rateHeader.RequiredApproval,

            Status = rateHeader.Status.ToString(),

            RateDetails = rateHeader.RateDetails.Select(From).ToList(),
        };

    public static object From(RateDetail detail) =>
        new
        {
            detail.Id,
            detail.RateHeaderId,
            detail.CostId,

            detail.Name,

            CostDetailType = detail.CostDetailType.ToString(),
            CostType = detail.CostType.ToString(),

            detail.CurrencyId,
            detail.CurrencyName,
            detail.CurrencyCode,

            detail.CostAmount,
            detail.SaleAmount,
            detail.UtilityAmount,
            detail.Quantity,

            detail.Notes,
        };
}
