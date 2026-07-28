using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Application.Imports;

public static class StandardizedImportFclRateFactory
{
    private static readonly HashSet<string> ReviewableImportIssueCodes = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "missing_agent",
        "unknown_agent",
        "missing_destination_port",
        "same_poe_and_pod",
    };

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

        var profile = extraction.ProfileReference;
        if (profile is null && sourceType != ImportSourceType.Email)
        {
            throw new InvalidOperationException(
                "Data Extraction no devolvió el perfil estandarizado de Config."
            );
        }

        var blockingIssuesByRecordId = extraction
            .Issues.Where(x => x.IsBlocking && x.PricingExtractionRecordId.HasValue)
            .GroupBy(x => x.PricingExtractionRecordId!.Value)
            .ToDictionary(x => x.Key, x => x.ToArray());

        var rates = new List<ImportFclRates>();
        var skippedRows = new List<Guid>();
        var rateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in extraction.Rows)
        {
            var canPersist = CanPersistReviewableRow(row, blockingIssuesByRecordId);

            if (!canPersist)
            {
                skippedRows.Add(row.Id);
                continue;
            }

            var rateKey = BuildRateKey(row);
            if (!rateKeys.Add(rateKey))
            {
                skippedRows.Add(row.Id);
                continue;
            }

            rates.Add(
                ImportFclRates.Create(
                    importBatchId,
                    row.Id,
                    sourceType,
                    ResolveSnapshot(profile, null),
                    ResolveSnapshot(row.OriginPortReference, row.OriginPort),
                    ResolveSnapshot(row.PortOfExitReference, row.PortOfExit),
                    ResolveSnapshot(row.DestinationPortReference, row.DestinationPort),
                    ResolveSnapshot(row.CarrierReference, row.Carrier),
                    ResolveSnapshot(row.AgentReference, row.Agent),
                    ResolveSnapshot(row.ContainerTypeReference, row.ContainerType),
                    ResolveSnapshot(row.CurrencyReference, row.Currency),
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

    private static bool CanPersistReviewableRow(
        DataExtractionFclPricingRow row,
        IReadOnlyDictionary<Guid, DataExtractionFclPricingIssue[]> blockingIssuesByRecordId
    )
    {
        var hasNonReviewableBlockingIssue =
            blockingIssuesByRecordId.TryGetValue(row.Id, out var rowIssues)
            && rowIssues.Any(x => !IsReviewableImportIssue(x.Code));

        return !hasNonReviewableBlockingIssue
            && HasText(row.OriginPort)
            && HasText(row.PortOfExit)
            && HasText(row.ContainerType)
            && HasText(row.Carrier)
            && HasText(row.Currency)
            && row.ValidFrom.HasValue
            && row.ValidTo.HasValue
            && row.ValidTo.Value >= row.ValidFrom.Value
            && (row.TotalSale.HasValue || row.OceanFreight.HasValue)
            && IsNonNegative(row.OceanFreight)
            && IsNonNegative(row.OriginCharges)
            && IsNonNegative(row.DestinationCharges)
            && IsNonNegative(row.Surcharges);
    }

    private static bool IsReviewableImportIssue(string code)
    {
        return ReviewableImportIssueCodes.Contains(code)
            || code.StartsWith("unknown_", StringComparison.OrdinalIgnoreCase);
    }

    private static CatalogSnapshot ResolveSnapshot(
        DataExtractionCatalogReference? reference,
        string? rawValue
    )
    {
        return reference is not null
            ? ToSnapshot(reference)
            : CatalogSnapshot.Unresolved(rawValue);
    }

    private static string BuildRateKey(DataExtractionFclPricingRow row)
    {
        return string.Join(
            "|",
            CatalogIdentity(row.OriginPortReference, row.OriginPort),
            CatalogIdentity(row.PortOfExitReference, row.PortOfExit),
            CatalogIdentity(row.CarrierReference, row.Carrier),
            CatalogIdentity(row.AgentReference, row.Agent),
            CatalogIdentity(row.ContainerTypeReference, row.ContainerType),
            CatalogIdentity(row.CurrencyReference, row.Currency),
            row.ValidFrom?.Date.Ticks,
            row.ValidTo?.Date.Ticks,
            row.OceanFreight,
            row.TotalSale
        );
    }

    private static string CatalogIdentity(
        DataExtractionCatalogReference? reference,
        string? rawValue
    )
    {
        return reference?.Id.ToString("N")
            ?? rawValue?.Trim().ToUpperInvariant()
            ?? string.Empty;
    }

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool IsNonNegative(decimal? value) => !value.HasValue || value.Value >= 0m;

    private static CatalogSnapshot ToSnapshot(DataExtractionCatalogReference reference)
    {
        return CatalogSnapshot.Create(reference.Id, reference.Name, reference.Code, reference.Slug);
    }
}

public sealed record StandardizedImportFclRateMappingResult(
    IReadOnlyCollection<ImportFclRates> Rates,
    IReadOnlyCollection<Guid> SkippedExtractionRowIds
);
