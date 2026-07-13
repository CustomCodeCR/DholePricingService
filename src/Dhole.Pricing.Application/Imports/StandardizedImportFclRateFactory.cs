using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
        "missing_port_of_exit",
        "missing_agent",
        "unknown_origin_port",
        "unknown_port_of_exit",
        "unknown_destination_port",
        "unknown_container_type",
        "unknown_carrier",
        "unknown_agent",
        "unknown_currency",
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

        foreach (var row in extraction.Rows)
        {
            var canPersist = CanPersistReviewableRow(row, blockingIssuesByRecordId);

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
                    profile is not null
                        ? ToSnapshot(profile)
                        : CreateFallbackSnapshot(
                            "pricing-imports-profiles",
                            "Email",
                            "EMAIL",
                            "Importación desde correo"
                        ),
                    ResolveSnapshot(row.OriginPortReference, "pol", row.OriginPort),
                    ResolveSnapshot(
                        row.PortOfExitReference,
                        "poe",
                        row.PortOfExit ?? row.OriginPortReference?.Name ?? row.OriginPort,
                        "PENDING",
                        "Por asignar"
                    ),
                    ResolveSnapshot(row.DestinationPortReference, "pod", row.DestinationPort),
                    ResolveSnapshot(row.CarrierReference, "carriers", row.Carrier),
                    ResolveSnapshot(
                        row.AgentReference,
                        "agents",
                        row.Agent,
                        "PENDING",
                        "Por asignar"
                    ),
                    ResolveSnapshot(
                        row.ContainerTypeReference,
                        "container-types",
                        row.ContainerType
                    ),
                    ResolveSnapshot(row.CurrencyReference, "currencies", row.Currency),
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
            && rowIssues.Any(x => !ReviewableImportIssueCodes.Contains(x.Code));

        return !hasNonReviewableBlockingIssue
            && HasText(row.OriginPort)
            && HasText(row.DestinationPort)
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

    private static CatalogSnapshot ResolveSnapshot(
        DataExtractionCatalogReference? reference,
        string catalogGroupSlug,
        string? rawValue,
        string? fallbackCode = null,
        string? fallbackName = null
    )
    {
        return reference is not null
            ? ToSnapshot(reference)
            : CreateFallbackSnapshot(catalogGroupSlug, rawValue, fallbackCode, fallbackName);
    }

    private static CatalogSnapshot CreateFallbackSnapshot(
        string catalogGroupSlug,
        string? rawValue,
        string? fallbackCode = null,
        string? fallbackName = null
    )
    {
        var hasRawValue = HasText(rawValue);
        var name = hasRawValue ? rawValue!.Trim() : fallbackName ?? "Por asignar";
        var normalized = NormalizeCatalogValue(name);
        var code =
            hasRawValue
                ? normalized.Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant()
            : HasText(fallbackCode) ? fallbackCode!.Trim().ToUpperInvariant()
            : normalized.Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var slug = string.IsNullOrWhiteSpace(normalized) ? "pending" : normalized;

        code = Limit(
            string.IsNullOrWhiteSpace(code) ? "PENDING" : code,
            catalogGroupSlug switch
            {
                "currencies" => 20,
                "container-types" => 50,
                _ => 100,
            }
        );
        name = Limit(name, catalogGroupSlug is "currencies" or "container-types" ? 150 : 250);
        slug = Limit(slug, 200);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{catalogGroupSlug}:{slug}"));
        var idBytes = hash.AsSpan(0, 16).ToArray();
        var id = new Guid(idBytes);

        return CatalogSnapshot.Create(id, name, code, slug);
    }

    private static string NormalizeCatalogValue(string value)
    {
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var appendSeparator = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (appendSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
                appendSeparator = false;
            }
            else
            {
                appendSeparator = true;
            }
        }

        return builder.ToString();
    }

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool IsNonNegative(decimal? value) => !value.HasValue || value.Value >= 0m;

    private static string Limit(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static CatalogSnapshot ToSnapshot(DataExtractionCatalogReference reference)
    {
        return CatalogSnapshot.Create(reference.Id, reference.Name, reference.Code, reference.Slug);
    }
}

public sealed record StandardizedImportFclRateMappingResult(
    IReadOnlyCollection<ImportFclRates> Rates,
    IReadOnlyCollection<Guid> SkippedExtractionRowIds
);
