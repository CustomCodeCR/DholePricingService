using Dhole.Pricing.Contracts.Imports.Response;
using Dhole.Pricing.Domain.Imports.Entities;

namespace Dhole.Pricing.Application.Features.Imports;

internal static class ImportRateMappings
{
    public static ImportRateDto ToDto(this ImportFclRates importRate)
    {
        return new ImportRateDto
        {
            Id = importRate.Id,
            ImportBatchId = importRate.ImportBatchId,
            ExtractionRecordId = importRate.ExtractionRecordId,
            SourceType = importRate.SourceType.ToString(),
            ImportProfileId = importRate.ImportProfileId,
            ImportProfileName = importRate.ImportProfileName,
            ImportProfileCode = importRate.ImportProfileCode,
            ImportProfileSlug = importRate.ImportProfileSlug,
            PolId = importRate.PolId,
            Pol = importRate.PolName,
            PolCode = importRate.PolCode,
            PolSlug = importRate.PolSlug,
            PoeId = importRate.PoeId,
            Poe = importRate.PoeName,
            PoeCode = importRate.PoeCode,
            PoeSlug = importRate.PoeSlug,
            PodId = importRate.PodId,
            Pod = importRate.PodName,
            PodCode = importRate.PodCode,
            PodSlug = importRate.PodSlug,
            CarrierId = importRate.CarrierId,
            Carrier = importRate.CarrierName,
            CarrierCode = importRate.CarrierCode,
            CarrierSlug = importRate.CarrierSlug,
            AgentId = importRate.AgentId,
            Agent = importRate.AgentName,
            AgentCode = importRate.AgentCode,
            AgentSlug = importRate.AgentSlug,
            ContainerTypeId = importRate.ContainerTypeId,
            ContainerType = importRate.ContainerTypeName,
            ContainerTypeCode = importRate.ContainerTypeCode,
            ContainerTypeSlug = importRate.ContainerTypeSlug,
            CurrencyId = importRate.CurrencyId,
            Currency = importRate.CurrencyName,
            CurrencyCode = importRate.CurrencyCode,
            CurrencySlug = importRate.CurrencySlug,
            Commodity = importRate.Commodity,
            Freight = importRate.OceanFreight ?? importRate.Freight,
            OceanFreight = importRate.OceanFreight,
            OriginCharges = importRate.OriginCharges,
            DestinationCharges = importRate.DestinationCharges,
            Surcharges = importRate.Surcharges,
            TotalCost = importRate.TotalCost,
            TotalSale = importRate.TotalSale,
            Profit = importRate.Profit,
            Margin = importRate.Margin,
            FreeDays = importRate.FreeDays,
            TransitDays = importRate.TransitDays,
            ValidFrom = importRate.ValidFrom,
            ValidTo = importRate.ValidTo,
            RawDataJson = importRate.RawDataJson ?? "{}",
            Status = importRate.Status.ToString(),
            UsedAsRateCount = importRate.UsedAsRateCount,
            CreatedAsRateHeaderId = importRate.CreatedAsRateHeaderId,
        };
    }

    public static ImportRateSelectDto ToSelectDto(this ImportFclRates importRate)
    {
        return new ImportRateSelectDto(
            importRate.Id,
            importRate.ImportBatchId,
            importRate.SourceType.ToString(),
            importRate.PolName,
            importRate.PodName,
            importRate.CarrierName,
            importRate.ContainerTypeName,
            importRate.CurrencyName,
            importRate.OceanFreight ?? importRate.Freight,
            importRate.FreeDays,
            importRate.ValidFrom,
            importRate.ValidTo,
            importRate.RawDataJson ?? string.Empty,
            importRate.Status.ToString(),
            importRate.UsedAsRateCount
        );
    }
}
