using System.Globalization;
using System.Text;
using System.Text.Json;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.MarketPricing.Normalization;
using Dhole.Pricing.Contracts.Competitors;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// Applies an explicit user-authored route expansion to an imported tariff.
/// The source PDF is immutable; each selected POL gets one observation for the
/// same source row, so a six-POL quote cannot become six samples in one lane.
/// Re-applying this operation is idempotent for the same source-row identifiers.
/// </summary>
internal static class CompetitorTariffRouteOverride
{
    public static async Task<IResult> ApplyAsync(
        Guid competitorTariffId,
        SetCompetitorTariffRoutesRequest request,
        ServiceDbContext dbContext,
        IMarketRateNormalizationService normalizationService,
        IPricingConfigCatalogClient catalogClient,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var poeIds = request.PoeIds is { Count: > 0 }
            ? request.PoeIds.Distinct().ToArray()
            : request.PoeId != Guid.Empty ? [request.PoeId] : Array.Empty<Guid>();
        if (request.PolIds is null || request.PolIds.Count > 25
            || request.PolIds.Any(x => x == Guid.Empty)
            || poeIds.Length is < 1 or > 10
            || poeIds.Any(x => x == Guid.Empty))
            return EndpointResults.BadRequest(
                "CompetitorTariff.InvalidRouteOverride",
                "Seleccione hasta 25 POL y entre 1 y 10 POE válidos. El POD permanecerá vacío.",
                httpContext
            );

        var polIds = request.PolIds.Distinct().ToArray();
        if (polIds.Length != request.PolIds.Count)
            return EndpointResults.BadRequest(
                "CompetitorTariff.DuplicatePol",
                "No se permiten POL duplicados.",
                httpContext
            );

        // Restrict route normalization to the explicitly selected Config origins.
        var selectedPols = new List<PricingConfigCatalogItem>(polIds.Length);
        foreach (var id in polIds)
        {
            var item = await catalogClient.GetActiveByIdAsync(id, cancellationToken);
            if (item is null
                || !(item.CatalogGroupSlug.Equals("pol", StringComparison.OrdinalIgnoreCase)
                    || item.CatalogGroupSlug.Equals("ports", StringComparison.OrdinalIgnoreCase)))
                return EndpointResults.BadRequest(
                    "CompetitorTariff.InvalidPol",
                    "Uno de los POL seleccionados no existe o no está activo en Config.",
                    httpContext
                );
            selectedPols.Add(item);
        }

        var tariff = await dbContext.CompetitorTariffs
            .FirstOrDefaultAsync(x => x.Id == competitorTariffId, cancellationToken);
        if (tariff is null)
            return Results.NotFound();

        if (tariff.ImportStatus == "Processing")
            return EndpointResults.BadRequest(
                "CompetitorTariff.ImportInProgress",
                "Espere a que finalice la extracción antes de modificar las rutas.",
                httpContext
            );

        var previous = await dbContext.CompetitorRateObservations
            .Where(x => x.CompetitorTariffId == competitorTariffId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (previous.Count == 0)
            return EndpointResults.BadRequest(
                "CompetitorTariff.ObservationsRequired",
                "Esta tarifa no tiene observaciones extraídas para aplicar las rutas.",
                httpContext
            );

        // Clones from a previous override retain SourceImportId. Regroup them so
        // a subsequent edit replaces the set instead of multiplying records.
        var sourceRows = previous
            .GroupBy(x => x.SourceImportId ?? x.Id)
            .Select(x => x.First())
            .ToArray();
        var replacements = new List<CompetitorRateObservation>();
        if (sourceRows.Length == 1 && polIds.Length == 0)
            return EndpointResults.BadRequest(
                "CompetitorTariff.MissingPol",
                "Seleccione al menos un POL para ampliar una cotización de una sola ruta.",
                httpContext
            );

        foreach (var source in sourceRows)
        {
            // Single-row quotes may deliberately expand commercial coverage.
            // A 96-row MGF matrix already identifies each published POL and
            // must NEVER be multiplied by all selected origins: prices differ
            // by carrier and origin.
            var originalPol = ReadOriginalPort(source.RawPayloadJson, "OriginPort");
            var sourcePolIds = sourceRows.Length == 1
                ? polIds.Cast<Guid?>().ToArray()
                : new Guid?[] { MatchSelectedPol(originalPol, source.PolId, selectedPols) };
            foreach (var polId in sourcePolIds)
            {
                foreach (var poeId in poeIds)
                {
                var normalized = await normalizationService.NormalizeAsync(
                    new MarketRateNormalizationRequest(
                        source.IncotermId ?? tariff.IncotermId,
                        source.IncotermCode,
                        polId,
                        null, // Never silently substitute an unselected origin.
                        poeId,
                        null,
                        null, // POE is the destination endpoint; no POD on this quote.
                        null,
                        source.CarrierId,
                        source.CarrierName,
                        source.ContainerTypeId,
                        source.ContainerTypeCode,
                        source.Mode.ToString(),
                        source.Mode,
                        source.Currency,
                        Math.Max(1, source.Quantity),
                        source.RateBasis,
                        null,
                        source.OceanFreight,
                        source.OriginCharges,
                        source.DestinationCharges,
                        source.InlandCharges,
                        source.OtherCharges,
                        source.OriginalAmount,
                        null, null, null, null, null, null,
                        source.ValidFrom
                    ),
                    cancellationToken
                );

                if ((sourceRows.Length == 1 && !normalized.Route.Pol.Matched) || !normalized.Route.Poe.Matched
                    || !normalized.Incoterm.Matched
                    || (source.Mode is ShipmentMode.Fcl or ShipmentMode.Ftl
                        && !normalized.Equipment.Matched)
                    || normalized.Issues.Any(issue => issue.IsBlocking
                        && !(sourceRows.Length > 1
                            && (issue.Code is "missing_pol" or "unresolved_pol" or "unresolved_pol_ambiguous"))))
                {
                    return EndpointResults.BadRequest(
                        "CompetitorTariff.RouteNormalizationFailed",
                        "No se pudo normalizar alguno de los POL o el POE indicado con los catálogos. No se guardaron cambios.",
                        httpContext
                    );
                }

                var rawPayload = JsonSerializer.Serialize(new
                {
                    SourceRawPayload = source.RawPayloadJson,
                    RouteOverride = new
                    {
                        Kind = "UserSpecifiedCommercialCoverage",
                        OriginalExtractedPol = source.PolName,
                        PolId = normalized.Route.Pol.Id,
                        OriginalPol = originalPol,
                        PoeId = poeId,
                        PodId = (Guid?)null,
                        AppliedAtUtc = DateTime.UtcNow
                    }
                });

                var observation = CompetitorRateObservation.Create(
                    competitorTariffId,
                    source.CompetitorCompanyId,
                    source.CompetitorCompanyName,
                    source.SourceDocumentId,
                    source.SourceImportId,
                    source.Mode,
                    source.Currency,
                    source.ValidFrom,
                    source.ValidTo,
                    source.RateBasis,
                    source.OceanFreight,
                    source.OriginCharges,
                    source.DestinationCharges,
                    source.InlandCharges,
                    source.OtherCharges,
                    source.OriginalAmount,
                    source.ExtractionConfidence, // Never approve a discrepancy automatically.
                    rawPayload,
                    source.ImportedAtUtc
                );

                var comparableAmount = normalized.NormalizedAllIn
                    ?? normalized.NormalizedOceanFreight;

                observation.ApplyNormalization(
                    normalized.Incoterm.Id,
                    normalized.Incoterm.Code,
                    normalized.Route.Pol.Id,
                    normalized.Route.Pol.Name,
                    normalized.Route.Pol.Code,
                    normalized.Route.Poe.Id,
                    normalized.Route.Poe.Name,
                    normalized.Route.Poe.Code,
                    null, null, null,
                    normalized.Carrier.Id,
                    normalized.Carrier.Name,
                    normalized.Carrier.Code,
                    normalized.Equipment.Id,
                    normalized.Equipment.Code,
                    source.Quantity,
                    normalized.NormalizedOceanFreight,
                    normalized.NormalizedAllIn,
                    normalized.Currency.NormalizedCurrency,
                    comparableAmount,
                    normalized.Currency.RateToUsd,
                    normalized.Currency.ExchangeRateDate,
                    normalized.NormalizationConfidence
                );

                replacements.Add(observation);
                }
            }
        }

        dbContext.CompetitorRateObservations.RemoveRange(previous);
        dbContext.CompetitorRateObservations.AddRange(replacements);

        var toReview = replacements.Count(x => !IsUsable(x));
        tariff.CompleteImport(
            replacements.Where(x => x.PolId.HasValue).Select(x => x.PolId!.Value).Distinct().ToArray(),
            replacements.Where(x => x.PoeId.HasValue).Select(x => x.PoeId!.Value).Distinct().ToArray(),
            Array.Empty<Guid>(), // Explicitly no POD
            replacements.Where(x => x.CarrierId.HasValue).Select(x => x.CarrierId!.Value).Distinct().ToArray(),
            replacements.Min(x => x.ValidFrom),
            replacements.Max(x => x.ValidTo),
            tariff.ExtractionExecutionId,
            replacements.Count,
            toReview,
            toReview == 0 ? "Processed" : "ReviewRequired"
        );

        await dbContext.SaveChangesAsync(cancellationToken);

        return EndpointResults.Ok(new
        {
            competitorTariffId,
            polIds,
            poeIds,
            podId = (Guid?)null,
            observationCount = replacements.Count,
            reviewCount = toReview,
            manuallyOverridden = true
        });
    }

    // For a multi-origin matrix, match every original source row to its
    // selected Config POL. An ambiguous/unmatched row remains in review:
    // the selected origins are NOT copied to other carriers' routes.
    internal static Guid? MatchSelectedPol(
        string? originalPol,
        Guid? existingPolId,
        IReadOnlyCollection<PricingConfigCatalogItem> selectedPols)
    {
        var source = NormalizePort(originalPol);
        if (source.Length > 0)
        {
            var matches = selectedPols.Where(port => PortMatches(source, port)).ToArray();
            if (matches.Length == 1) return matches[0].Id;
        }
        return existingPolId.HasValue && selectedPols.Any(port => port.Id == existingPolId.Value)
            ? existingPolId : null;
    }

    private static bool PortMatches(string source, PricingConfigCatalogItem item)
    {
        var labels = new[] { item.Name, item.Value, item.Slug, item.Code }
            .Where(value => !string.IsNullOrWhiteSpace(value));
        foreach (var label in labels.Concat(ReadPortAliases(item.MetadataJson)))
        {
            var candidate = NormalizePort(label);
            if (candidate.Length == 0) continue;
            if (candidate == source
                || candidate.StartsWith(source + " ", StringComparison.Ordinal)
                || source.StartsWith(candidate + " ", StringComparison.Ordinal)
                || (" " + candidate + " ").Contains(" " + source + " ", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static IReadOnlyCollection<string> ReadPortAliases(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(metadata);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Array.Empty<string>();
            var aliases = new List<string>();
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (NormalizePort(property.Name).Replace(" ", "", StringComparison.Ordinal)
                    is not ("ALIAS" or "ALIASES" or "SYNONYM" or "SYNONYMS"
                        or "ALTERNATENAME" or "ALTERNATENAMES" or "UNLOCODE"))
                    continue;
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) aliases.Add(value);
                }
                else if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var alias in property.Value.EnumerateArray())
                        if (alias.ValueKind == JsonValueKind.String
                            && !string.IsNullOrWhiteSpace(alias.GetString()))
                            aliases.Add(alias.GetString()!);
                }
            }
            return aliases;
        }
        catch (JsonException) { return Array.Empty<string>(); }
    }

    private static string NormalizePort(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder();
        var separator = false;
        foreach (var ch in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            if (!char.IsLetterOrDigit(ch)) { separator = true; continue; }
            if (separator && builder.Length > 0) builder.Append(' ');
            builder.Append(char.ToUpperInvariant(ch));
            separator = false;
        }
        return builder.ToString();
    }

    internal static string? ReadOriginalPort(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return FindSourceValue(document.RootElement, key, 0);
        }
        catch (JsonException) { return null; }
    }

    private static string? FindSourceValue(JsonElement element, string key, int depth)
    {
        if (depth > 4 || element.ValueKind != JsonValueKind.Object) return null;
        if (element.TryGetProperty(key, out var port)
            && port.ValueKind == JsonValueKind.String)
            return port.GetString();
        foreach (var nestedKey in new[] { "RawJson", "SourceRawPayload" })
        {
            if (!element.TryGetProperty(nestedKey, out var child)) continue;
            if (child.ValueKind == JsonValueKind.Object)
            {
                var nested = FindSourceValue(child, key, depth + 1);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
            else if (child.ValueKind == JsonValueKind.String)
            {
                var nested = ReadOriginalPort(child.GetString(), key);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        return null;
    }

    private static bool IsUsable(CompetitorRateObservation item) =>
        item.ExtractionConfidence > 0m
        && item.IncotermId.HasValue
        && item.PolId.HasValue
        && (item.NormalizedAllIn.HasValue || item.NormalizedOceanFreight.HasValue)
        && string.Equals(item.NormalizedCurrency, "USD", StringComparison.OrdinalIgnoreCase)
        && (item.Mode is not (ShipmentMode.Fcl or ShipmentMode.Ftl) || item.ContainerTypeId.HasValue);
}
