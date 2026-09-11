using System.Text.Json;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Contracts.Imports.Request;

namespace Dhole.Pricing.Application.Imports;

public static class DataExtractionPricingImportMapper
{
    private static readonly HashSet<string> SpotValidityIssueCodes = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "missing_valid_from",
        "missing_valid_to",
        "invalid_validity_range",
    };

    public static DataExtractionFclPricingResult ToApplicationResult(
        ExtractedPricingDataRequest response,
        Guid extractionExecutionId,
        Guid pricingImportId
    )
    {
        var spotRowIds = response.Rows
            .Where(IsSpotRate)
            .Select(row => row.Id)
            .ToHashSet();

        return new DataExtractionFclPricingResult(
            response.Success,
            response.ExtractionExecutionId ?? extractionExecutionId,
            pricingImportId,
            response.CorrelationId,
            new DataExtractionFclPricingSummary(
                response.Summary.TotalRows,
                response.Summary.ValidRows,
                response.Summary.WarningRows,
                response.Summary.InvalidRows,
                response.Summary.HasIssues
            ),
            response.Rows.Select(ToApplicationRow).ToArray(),
            response.Issues.Select(issue => ToApplicationIssue(issue, spotRowIds)).ToArray(),
            response.ErrorCode,
            response.ErrorMessage,
            ToApplicationReference(response.ProfileReference)
        );
    }

    private static DataExtractionFclPricingRow ToApplicationRow(
        ExtractedPricingRowRequest row
    )
    {
        var isSpot = IsSpotRate(row);
        var validFrom = row.ValidFrom;
        var validTo = row.ValidTo;
        var commodity = FirstText(
            row.Commodity,
            ReadRawValue(
                row.RawJson,
                "commodity",
                "mercancia",
                "producto",
                "cargo",
                "cargotype",
                "descripcion",
                "description"
            )
        );
        var spaceComment = row.SpaceComment;

        if (isSpot)
        {
            var spotDate = GetCostaRicaToday();
            validFrom = spotDate;
            validTo = spotDate;

            var etd = ReadRawValue(
                row.RawJson,
                "etd",
                "fechaetd",
                "estimateddeparture",
                "estimatedtimeofdeparture"
            );

            spaceComment = MergeComments(
                row.SpaceComment,
                row.Remarks,
                HasText(etd) ? $"ETD: {etd!.Trim()}" : null,
                HasText(commodity) ? $"Commodity: {commodity!.Trim()}" : null
            );
        }

        return new DataExtractionFclPricingRow(
            row.Id,
            row.SourceSheetName,
            row.SourceRowNumber,
            row.OriginPort,
            row.PortOfExit,
            row.DestinationPort,
            row.ContainerType,
            row.Carrier,
            row.Agent,
            commodity,
            row.Currency,
            row.FreeDays,
            row.TransitDays,
            validFrom,
            validTo,
            row.OceanFreight,
            row.OriginCharges,
            row.DestinationCharges,
            row.Surcharges,
            row.TotalCost,
            row.TotalSale,
            row.Profit,
            row.Margin,
            spaceComment,
            row.Remarks,
            row.Status,
            row.RawJson,
            ToApplicationReference(row.OriginPortReference),
            ToApplicationReference(row.PortOfExitReference),
            ToApplicationReference(row.DestinationPortReference),
            ToApplicationReference(row.ContainerTypeReference),
            ToApplicationReference(row.CarrierReference),
            ToApplicationReference(row.AgentReference),
            ToApplicationReference(row.CurrencyReference)
        );
    }

    private static DataExtractionFclPricingIssue ToApplicationIssue(
        ExtractedPricingIssueRequest issue,
        IReadOnlySet<Guid> spotRowIds
    )
    {
        var isRecoverableSpotValidityIssue =
            issue.ExtractedPricingRowId.HasValue
            && spotRowIds.Contains(issue.ExtractedPricingRowId.Value)
            && SpotValidityIssueCodes.Contains(issue.Code);

        return new DataExtractionFclPricingIssue(
            issue.Id,
            issue.ExtractedPricingRowId,
            issue.Code,
            issue.Message,
            isRecoverableSpotValidityIssue ? false : issue.IsBlocking,
            issue.SourceSheetName,
            issue.SourceRowNumber,
            issue.ColumnName,
            issue.RawValue
        );
    }

    private static bool IsSpotRate(ExtractedPricingRowRequest row)
    {
        var rateType = ReadRawValue(
            row.RawJson,
            "tipotarifa",
            "tipodetarifa",
            "ratetype",
            "tarifftype"
        );

        return string.Equals(rateType?.Trim(), "SPOT", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadRawValue(string? rawJson, params string[] normalizedAliases)
    {
        if (!HasText(rawJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson!);
            return FindJsonValue(document.RootElement, normalizedAliases);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FindJsonValue(
        JsonElement element,
        IReadOnlyCollection<string> normalizedAliases
    )
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (
                    normalizedAliases.Contains(
                        NormalizeKey(property.Name),
                        StringComparer.OrdinalIgnoreCase
                    )
                    && property.Value.ValueKind is not JsonValueKind.Null
                    && property.Value.ValueKind is not JsonValueKind.Undefined
                )
                {
                    var value = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.ToString();

                    if (HasText(value))
                    {
                        return value!.Trim();
                    }
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var nested = FindJsonValue(property.Value, normalizedAliases);
                if (HasText(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindJsonValue(item, normalizedAliases);
                if (HasText(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string NormalizeKey(string value)
    {
        return new string(
            value.Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray()
        );
    }

    private static string? MergeComments(params string?[] comments)
    {
        var merged = new List<string>();

        foreach (var comment in comments)
        {
            if (!HasText(comment))
            {
                continue;
            }

            var normalized = comment!.Trim();
            if (
                merged.Any(existing =>
                    existing.Equals(normalized, StringComparison.OrdinalIgnoreCase)
                    || existing.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                continue;
            }

            merged.Add(normalized);
        }

        return merged.Count == 0 ? null : string.Join(" | ", merged);
    }

    private static string? FirstText(params string?[] values)
    {
        return values.FirstOrDefault(HasText)?.Trim();
    }

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static DateTime GetCostaRicaToday()
    {
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Costa_Rica");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone).Date;
        }
        catch (TimeZoneNotFoundException)
        {
            return DateTime.UtcNow.Date;
        }
        catch (InvalidTimeZoneException)
        {
            return DateTime.UtcNow.Date;
        }
    }

    private static DataExtractionCatalogReference? ToApplicationReference(
        ExtractedCatalogReferenceRequest? reference
    )
    {
        return reference is null
            ? null
            : new DataExtractionCatalogReference(
                reference.Id,
                reference.CatalogGroupSlug,
                reference.Code,
                reference.Slug,
                reference.Name,
                reference.RawValue
            );
    }
}
