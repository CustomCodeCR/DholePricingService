using Dhole.Pricing.Contracts.Imports.Response;
using Dhole.Pricing.Domain.Imports.Entities;

namespace Dhole.Pricing.Application.Features.Imports;

internal static class ImportRateMappings
{
    public static ImportRateDto ToDto(this ImportFclRates importRate)
    {
        return new ImportRateDto(
            importRate.Id,
            importRate.ImportBatchId,
            importRate.SourceType.ToString(),
            importRate.Pol,
            importRate.Pod,
            importRate.Carrier,
            importRate.ContainerType,
            importRate.Currency,
            importRate.Freight,
            importRate.FreeDays,
            importRate.ValidFrom,
            importRate.ValidTo,
            importRate.Status.ToString(),
            importRate.RawDataJson ?? string.Empty,
            importRate.UsedAsRateCount
        );
    }

    public static ImportRateSelectDto ToSelectDto(this ImportFclRates importRate)
    {
        return new ImportRateSelectDto(
            importRate.Id,
            importRate.ImportBatchId,
            importRate.SourceType.ToString(),
            importRate.Pol,
            importRate.Pod,
            importRate.Carrier,
            importRate.ContainerType,
            importRate.Currency,
            importRate.Freight,
            importRate.FreeDays,
            importRate.ValidFrom,
            importRate.ValidTo,
            importRate.RawDataJson ?? string.Empty,
            importRate.Status.ToString(),
            importRate.UsedAsRateCount
        );
    }
}
