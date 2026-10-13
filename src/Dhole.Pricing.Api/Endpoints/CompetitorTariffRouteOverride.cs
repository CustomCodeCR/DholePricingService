using System.Text.Json;
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
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (request.PolIds is null || request.PolIds.Count is < 1 or > 25
            || request.PolIds.Any(x => x == Guid.Empty)
            || request.PoeId == Guid.Empty)
            return EndpointResults.BadRequest(
                "CompetitorTariff.InvalidRouteOverride",
                "Seleccione entre 1 y 25 POL y un POE válido. El POD permanecerá vacío.",
                httpContext
            );

        var polIds = request.PolIds.Distinct().ToArray();
        if (polIds.Length != request.PolIds.Count)
            return EndpointResults.BadRequest(
                "CompetitorTariff.DuplicatePol",
                "No se permiten POL duplicados.",
                httpContext
            );

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

        foreach (var source in sourceRows)
        {
            foreach (var polId in polIds)
            {
                var normalized = await normalizationService.NormalizeAsync(
                    new MarketRateNormalizationRequest(
                        source.IncotermId ?? tariff.IncotermId,
                        source.IncotermCode,
                        polId,
                        null,
                        request.PoeId,
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

                if (!normalized.Route.Pol.Matched || !normalized.Route.Poe.Matched
                    || !normalized.Incoterm.Matched
                    || (source.Mode is ShipmentMode.Fcl or ShipmentMode.Ftl
                        && !normalized.Equipment.Matched)
                    || !normalized.Success)
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
                        PolId = polId,
                        PoeId = request.PoeId,
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
            poeId = request.PoeId,
            podId = (Guid?)null,
            observationCount = replacements.Count,
            reviewCount = toReview,
            manuallyOverridden = true
        });
    }

    private static bool IsUsable(CompetitorRateObservation item) =>
        item.ExtractionConfidence > 0m
        && item.IncotermId.HasValue
        && item.PolId.HasValue
        && (item.NormalizedAllIn.HasValue || item.NormalizedOceanFreight.HasValue)
        && string.Equals(item.NormalizedCurrency, "USD", StringComparison.OrdinalIgnoreCase)
        && (item.Mode is not (ShipmentMode.Fcl or ShipmentMode.Ftl) || item.ContainerTypeId.HasValue);
}
