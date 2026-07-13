using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Application.Imports;

public static class StandardizedImportFclRateFactory
{
    public static StandardizedImportFclRateMappingResult CreateRates(
        Guid importBatchId,
        ImportSourceType sourceType,
        DataExtractionFclPricingResult extraction,
        Guid? createdBy
    )
    {
        if (!extraction.Success)
        {
            throw new InvalidOperationException(
                extraction.ErrorMessage ?? "Data Extraction no pudo procesar el archivo."
            );
        }

        var profile =
            extraction.ProfileReference
            ?? throw new InvalidOperationException(
                "Data Extraction no devolvió el perfil estandarizado de Config."
            );

        var blockingRecordIds = extraction
            .Issues.Where(x => x.IsBlocking && x.PricingExtractionRecordId.HasValue)
            .Select(x => x.PricingExtractionRecordId!.Value)
            .ToHashSet();

        var rates = new List<ImportFclRates>();
        var skippedRows = new List<Guid>();

        foreach (var row in extraction.Rows)
        {
            var canPersist =
                !blockingRecordIds.Contains(row.Id)
                && string.Equals(row.Status, "Valid", StringComparison.OrdinalIgnoreCase)
                && row.HasAllRequiredCatalogReferences
                && row.ValidFrom.HasValue
                && row.ValidTo.HasValue;

            if (!canPersist)
            {
                skippedRows.Add(row.Id);
                continue;
            }

            rates.Add(
                ImportFclRates.Create(
                    importBatchId,
                    row.Id,
                    sourceType,
                    ToSnapshot(profile),
                    ToSnapshot(row.OriginPortReference!),
                    ToSnapshot(row.PortOfExitReference!),
                    ToSnapshot(row.DestinationPortReference!),
                    ToSnapshot(row.CarrierReference!),
                    ToSnapshot(row.AgentReference!),
                    ToSnapshot(row.ContainerTypeReference!),
                    ToSnapshot(row.CurrencyReference!),
                    row.Commodity,
                    row.OceanFreight,
                    row.OriginCharges,
                    row.DestinationCharges,
                    row.Surcharges,
                    row.TotalCost,
                    row.TotalSale,
                    row.Profit,
                    row.Margin,
                    row.FreeDays ?? 0,
                    row.TransitDays ?? 0,
                    row.ValidFrom!.Value,
                    row.ValidTo!.Value,
                    row.RawJson,
                    createdBy
                )
            );
        }

        return new StandardizedImportFclRateMappingResult(rates, skippedRows);
    }

    private static CatalogSnapshot ToSnapshot(DataExtractionCatalogReference reference)
    {
        return CatalogSnapshot.Create(reference.Id, reference.Name, reference.Code, reference.Slug);
    }
}

public sealed record StandardizedImportFclRateMappingResult(
    IReadOnlyCollection<ImportFclRates> Rates,
    IReadOnlyCollection<Guid> SkippedExtractionRowIds
);
