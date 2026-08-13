using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        // Regla de negocio: cuando Data Extraction no pudo determinar la moneda,
        // Pricing asume USD y permite importar la fila.
        "missing_currency",
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

        foreach (var sourceRow in extraction.Rows)
        {
            var row = RecoverStructuralFields(sourceRow, sourceType);
            var promoteDestinationToPoe = ShouldPromoteEmailDestinationToPoe(
                row,
                sourceType
            );
            var resolvedPortOfExit = promoteDestinationToPoe
                ? row.DestinationPort
                : row.PortOfExit;
            var resolvedDestinationPort = promoteDestinationToPoe
                ? null
                : row.DestinationPort;
            var canPersist = CanPersistReviewableRow(
                row,
                blockingIssuesByRecordId,
                sourceType,
                resolvedPortOfExit
            );

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
                    ResolveOptionalSnapshot(row.OriginPortReference, "pol", row.OriginPort),
                    ResolveOptionalSnapshot(
                        row.PortOfExitReference,
                        "poe",
                        resolvedPortOfExit
                    ),
                    ResolveOptionalSnapshot(
                        promoteDestinationToPoe ? null : row.DestinationPortReference,
                        "pod",
                        resolvedDestinationPort,
                        "PENDING",
                        "Por asignar"
                    ),
                    ResolveOptionalSnapshot(row.CarrierReference, "carriers", row.Carrier),
                    ResolveOptionalSnapshot(
                        row.AgentReference,
                        "agents",
                        row.Agent,
                        "PENDING",
                        "Por asignar"
                    ),
                    ResolveOptionalSnapshot(
                        row.ContainerTypeReference,
                        "container-types",
                        row.ContainerType
                    ),
                    ResolveCurrencySnapshot(row.CurrencyReference, row.Currency),
                    row.Commodity,
                    row.SpaceComment,
                    RoundNumeric18Scale4(row.OceanFreight),
                    RoundNumeric18Scale4(row.OriginCharges),
                    RoundNumeric18Scale4(row.DestinationCharges),
                    RoundNumeric18Scale4(row.Surcharges),
                    RoundNumeric18Scale4(row.TotalCost),
                    RoundNumeric18Scale4(row.TotalSale),
                    RoundNumeric18Scale4(row.Profit),
                    RoundNumeric18Scale4(row.Margin),
                    row.FreeDays ?? 0,
                    row.TransitDays ?? 0,
                    row.ValidFrom!.Value,
                    row.ValidTo!.Value,
                    BuildPersistedRawJson(row),
                    createdBy
                )
            );
        }

        return new StandardizedImportFclRateMappingResult(rates, skippedRows);
    }

    private static bool CanPersistReviewableRow(
        DataExtractionFclPricingRow row,
        IReadOnlyDictionary<Guid, DataExtractionFclPricingIssue[]> blockingIssuesByRecordId,
        ImportSourceType sourceType,
        string? resolvedPortOfExit
    )
    {
        var hasNonReviewableBlockingIssue =
            blockingIssuesByRecordId.TryGetValue(row.Id, out var rowIssues)
            && rowIssues.Any(issue =>
                !IsReviewableImportIssue(issue.Code)
                && !IsRecoverableEmailIssue(issue.Code, row, sourceType)
            );

        return !hasNonReviewableBlockingIssue
            && HasText(row.OriginPort)
            && HasText(resolvedPortOfExit)
            && HasText(row.ContainerType)
            && HasText(row.Carrier)
            && row.ValidFrom.HasValue
            && row.ValidTo.HasValue
            && row.ValidTo.Value >= row.ValidFrom.Value
            && (row.TotalSale.HasValue || row.OceanFreight.HasValue)
            && IsNonNegative(row.OceanFreight)
            && IsNonNegative(row.OriginCharges)
            && IsNonNegative(row.DestinationCharges)
            && IsNonNegative(row.Surcharges)
            && FitsNumeric18Scale4(row.OceanFreight)
            && FitsNumeric18Scale4(row.OriginCharges)
            && FitsNumeric18Scale4(row.DestinationCharges)
            && FitsNumeric18Scale4(row.Surcharges)
            && FitsNumeric18Scale4(row.TotalCost)
            && FitsNumeric18Scale4(row.TotalSale)
            && FitsNumeric18Scale4(row.Profit)
            && FitsNumeric18Scale4(row.Margin);
    }

    private static bool ShouldPromoteEmailDestinationToPoe(
        DataExtractionFclPricingRow row,
        ImportSourceType sourceType
    )
    {
        return sourceType == ImportSourceType.Email
            && !HasText(row.PortOfExit)
            && HasText(row.DestinationPort);
    }

    private static bool IsRecoverableEmailIssue(
        string code,
        DataExtractionFclPricingRow row,
        ImportSourceType sourceType
    )
    {
        if (sourceType != ImportSourceType.Email)
        {
            return false;
        }

        return (
                code.Equals("missing_origin_port", StringComparison.OrdinalIgnoreCase)
                && HasText(row.OriginPort)
            )
            || (
                code.Equals("missing_port_of_exit", StringComparison.OrdinalIgnoreCase)
                && (HasText(row.PortOfExit) || ShouldPromoteEmailDestinationToPoe(row, sourceType))
            )
            || (
                code.Equals("missing_container_type", StringComparison.OrdinalIgnoreCase)
                && HasText(row.ContainerType)
            )
            || (
                code.Equals("missing_carrier", StringComparison.OrdinalIgnoreCase)
                && HasText(row.Carrier)
            );
    }

    private static bool IsReviewableImportIssue(string code)
    {
        return ReviewableImportIssueCodes.Contains(code)
            || code.StartsWith("unknown_", StringComparison.OrdinalIgnoreCase);
    }

    private static DataExtractionFclPricingRow RecoverStructuralFields(
        DataExtractionFclPricingRow row,
        ImportSourceType sourceType
    )
    {
        if (string.IsNullOrWhiteSpace(row.RawJson))
        {
            return row;
        }

        var originPort = FirstText(
            row.OriginPort,
            ReadRawJsonValue(row.RawJson, "OriginPort", "POL", "pol", "PortOfLoading")
        );
        var portOfExit = FirstText(
            row.PortOfExit,
            ReadRawJsonValue(row.RawJson, "PortOfExit", "POE", "poe", "PortOfDischarge"),
            sourceType == ImportSourceType.Email
                ? ReadRawJsonValue(row.RawJson, "POD", "pod", "DestinationPort")
                : null
        );
        var destinationPort = FirstText(
            row.DestinationPort,
            sourceType == ImportSourceType.Email
                ? ReadRawJsonValue(row.RawJson, "FinalDestination", "PlaceOfDelivery")
                : ReadRawJsonValue(
                    row.RawJson,
                    "DestinationPort",
                    "FinalDestination",
                    "PlaceOfDelivery"
                )
        );
        var containerType = FirstText(
            row.ContainerType,
            ReadRawJsonValue(
                row.RawJson,
                "ContainerType",
                "ContainerSize",
                "EquipmentType",
                "Equipment"
            )
        );
        var carrier = FirstText(
            row.Carrier,
            ReadRawJsonValue(row.RawJson, "Carrier", "ShippingLine", "Naviera")
        );

        return row with
        {
            OriginPort = originPort,
            PortOfExit = portOfExit,
            DestinationPort = destinationPort,
            ContainerType = containerType,
            Carrier = carrier,
        };
    }

    private static string? BuildPersistedRawJson(DataExtractionFclPricingRow row)
    {
        if (!HasText(row.SpaceComment) && !HasText(row.Remarks))
        {
            return row.RawJson;
        }

        object? rawPayload = null;
        string? rawText = null;
        if (HasText(row.RawJson))
        {
            try
            {
                using var document = JsonDocument.Parse(row.RawJson!);
                rawPayload = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                rawText = row.RawJson;
            }
        }

        return JsonSerializer.Serialize(new
        {
            SpaceComment = FirstText(row.SpaceComment, row.Remarks),
            Remarks = row.Remarks,
            Raw = rawPayload,
            RawText = rawText,
        });
    }

    private static string? ReadRawJsonValue(string rawJson, params string[] aliases)
    {
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var alias in aliases)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!property.Name.Equals(alias, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var value = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.ValueKind is JsonValueKind.Number
                            ? property.Value.GetRawText()
                            : null;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // RawJson is diagnostic data. A malformed payload must not abort the import.
        }

        return null;
    }

    private static string? FirstText(params string?[] values)
    {
        return values.FirstOrDefault(HasText)?.Trim();
    }

    private static CatalogSnapshot ResolveOptionalSnapshot(
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

    private static CatalogSnapshot ResolveCurrencySnapshot(
        DataExtractionCatalogReference? reference,
        string? rawValue
    )
    {
        if (reference is not null)
        {
            return ToSnapshot(reference);
        }

        var currency = HasText(rawValue) ? rawValue!.Trim().ToUpperInvariant() : "USD";
        return CreateFallbackSnapshot(
            "currencies",
            currency,
            currency,
            currency
        );
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

    private const decimal MaximumNumeric18Scale4 = 99_999_999_999_999.9999m;

    private static bool IsNonNegative(decimal? value) => !value.HasValue || value.Value >= 0m;

    private static bool FitsNumeric18Scale4(decimal? value) =>
        !value.HasValue || Math.Abs(value.Value) <= MaximumNumeric18Scale4;

    private static decimal? RoundNumeric18Scale4(decimal? value) =>
        value.HasValue
            ? decimal.Round(value.Value, 4, MidpointRounding.AwayFromZero)
            : null;

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
