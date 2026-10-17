using System.Text.Json;
using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.MarketPricing.Normalization;
using Dhole.Pricing.Domain.Competitors.Entities;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Domain.MarketPricing.Enums;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Services;

public sealed record CompetitorTariffImportWorkItem(
    Guid CompetitorTariffId, Guid IncotermId, string CompetitorCompanyName,
    ShipmentMode ShipmentMode, DateTime? FallbackFrom, DateTime? FallbackTo,
    string OriginalFileName, string? ContentType, string? FileExtension,
    long FileSizeBytes, string FileHash, Guid? RequestedBy,
    string? RequestedByName, byte[] FileContent, Guid StorageId);

public sealed class CompetitorExtractionException(
    string code, string? message, Guid? executionId, bool transient)
    : Exception(message ?? code)
{
    public string ErrorCode { get; } = code;
    public Guid? ExecutionId { get; } = executionId;
    public bool IsTransient { get; } = transient;
}

/// <summary>
/// Recovers imports from PostgreSQL, with atomic leases that prevent duplicate
/// processing across replicas and survive API deployments and restarts.
/// </summary>
public sealed class CompetitorTariffImportWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<CompetitorTariffImportWorker> logger) : BackgroundService
{
    private const int MaxAttempts = 2;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();
                var now = DateTime.UtcNow;
                var ready = await db.CompetitorTariffImportPayloads
                    .AsNoTracking()
                    .Where(p => p.NextAttemptAtUtc <= now
                        && (p.LeaseUntilUtc == null || p.LeaseUntilUtc < now)
                        && db.CompetitorTariffs.Any(t =>
                            t.Id == p.CompetitorTariffId && t.ImportStatus == "Processing"))
                    .OrderBy(p => p.NextAttemptAtUtc)
                    .Select(p => p.CompetitorTariffId)
                    .Take(4)
                    .ToArrayAsync(stoppingToken);

                foreach (var id in ready)
                    await ProcessOneAsync(id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Competitor PDF import recovery scan failed.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task ProcessOneAsync(Guid tariffId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();
        var now = DateTime.UtcNow;
        var claimed = await db.CompetitorTariffImportPayloads
            .Where(p => p.CompetitorTariffId == tariffId
                && p.NextAttemptAtUtc <= now
                && (p.LeaseUntilUtc == null || p.LeaseUntilUtc < now))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(p => p.LeaseUntilUtc, (DateTime?)now.AddMinutes(23))
                .SetProperty(p => p.AttemptCount, p => p.AttemptCount + 1),
                cancellationToken);
        if (claimed != 1) return;

        var payload = await db.CompetitorTariffImportPayloads.AsNoTracking()
            .FirstOrDefaultAsync(p => p.CompetitorTariffId == tariffId, cancellationToken);
        var tariff = await db.CompetitorTariffs.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tariffId, cancellationToken);
        if (payload is null || tariff?.IncotermId is null || tariff.ImportStatus != "Processing")
            return;

        var item = new CompetitorTariffImportWorkItem(
            tariffId, tariff.IncotermId.Value, tariff.CompetitorCompanyName,
            tariff.ShipmentMode, payload.FallbackFrom, payload.FallbackTo,
            tariff.OriginalFileName ?? "competitor.pdf", payload.ContentType,
            Path.GetExtension(tariff.OriginalFileName),
            payload.FileContent.LongLength, payload.FileHash,
            payload.RequestedBy, payload.RequestedByName, payload.FileContent, tariff.StorageId);
        try
        {
            await scope.ServiceProvider.GetRequiredService<CompetitorTariffImportProcessor>()
                .ProcessAsync(item, cancellationToken);
            var status = await db.CompetitorTariffs.AsNoTracking()
                .Where(t => t.Id == tariffId).Select(t => t.ImportStatus)
                .FirstAsync(cancellationToken);
            if (status != "Processing")
            {
                // Original PDF is already permanently held in DholeStorage.
                await db.CompetitorTariffImportPayloads
                    .Where(p => p.CompetitorTariffId == tariffId)
                    .ExecuteDeleteAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Retain bytes; after the lease expires another instance resumes it.
            throw;
        }
        catch (Exception exception)
        {
            var extractionError = exception as CompetitorExtractionException;
            var code = extractionError?.ErrorCode ?? "Competitor.ImportUnhandled";
            var attempts = payload.AttemptCount;
            var retry = extractionError?.IsTransient == true && attempts < MaxAttempts;
            logger.LogWarning(exception,
                "Competitor import {TariffId}: attempt {Attempt}/{Max}, retry={Retry}, code={Code}.",
                tariffId, attempts, MaxAttempts, retry, code);
            db.ChangeTracker.Clear();
            var current = await db.CompetitorTariffs
                .FirstOrDefaultAsync(t => t.Id == tariffId, CancellationToken.None);
            if (current is null) return;
            if (retry)
            {
                current.RecordImportIssue(code, exception.Message);
                await db.CompetitorTariffImportPayloads
                    .Where(p => p.CompetitorTariffId == tariffId)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(p => p.LeaseUntilUtc, (DateTime?)null)
                        .SetProperty(p => p.NextAttemptAtUtc,
                            DateTime.UtcNow.AddSeconds(15 * attempts * attempts)),
                        CancellationToken.None);
            }
            else
            {
                current.MarkImportFailed(extractionError?.ExecutionId, 1, code, exception.Message);
                await db.CompetitorTariffImportPayloads
                    .Where(p => p.CompetitorTariffId == tariffId)
                    .ExecuteUpdateAsync(u => u.SetProperty(p => p.LeaseUntilUtc, (DateTime?)null),
                        CancellationToken.None);
            }
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }
}

public sealed class CompetitorTariffImportProcessor(
    ServiceDbContext dbContext,
    IDataExtractionFclPricingClient dataExtractionClient,
    IMarketRateNormalizationService normalizationService,
    ILogger<CompetitorTariffImportProcessor> logger
)
{
    public async Task ProcessAsync(
        CompetitorTariffImportWorkItem workItem,
        CancellationToken cancellationToken
    )
    {
        var entity = await dbContext.CompetitorTariffs
            .FirstOrDefaultAsync(
                x => x.Id == workItem.CompetitorTariffId,
                cancellationToken
            );

        if (entity is null)
            return;

        try
        {
            var extraction = await dataExtractionClient.ExtractAsync(
                new DataExtractionFclPricingRequest(
                    workItem.CompetitorTariffId,
                    $"competitor-{workItem.CompetitorTariffId:N}-{Guid.NewGuid():N}",
                    workItem.OriginalFileName,
                    workItem.ContentType,
                    workItem.FileExtension,
                    workItem.FileSizeBytes,
                    workItem.FileHash,
                    workItem.ShipmentMode == ShipmentMode.Fcl ? "COMPETITOR_FCL" : null,
                    workItem.RequestedBy,
                    workItem.RequestedByName,
                    workItem.FileContent,
                    workItem.StorageId != Guid.Empty
                        ? $"storage://{workItem.StorageId:D}" : null
                ),
                cancellationToken
            );

            if (!extraction.Success)
            {
                var code = extraction.ErrorCode ?? "DataExtraction.Failed";
                var retry = code is "Grpc.Unavailable" or "Grpc.DeadlineExceeded"
                    or "Grpc.ResourceExhausted" or "Grpc.Internal" or "Grpc.Cancelled"
                    || code.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
                    || code.Contains("502", StringComparison.OrdinalIgnoreCase);
                throw new CompetitorExtractionException(
                    code, extraction.ErrorMessage, extraction.ExtractionExecutionId, retry);
            }
            if (extraction.Rows.Count == 0)
            {
                throw new CompetitorExtractionException(
                    "DataExtraction.NoRows",
                    "El extractor devolvió cero filas; revise el PDF original o el formato del documento.",
                    extraction.ExtractionExecutionId, false);
            }

            var observations = new List<CompetitorRateObservation>();
            var reviewCount = 0;

            foreach (var row in extraction.Rows)
            {
                var validFrom = row.ValidFrom.HasValue
                    ? NormalizeUtc(row.ValidFrom.Value)
                    : workItem.FallbackFrom;
                var validTo = row.ValidTo.HasValue
                    ? NormalizeUtc(row.ValidTo.Value)
                    : workItem.FallbackTo ?? validFrom;

                // Keep partially extracted rows in the review queue instead of
                // silently discarding them. Provisional values are NEVER benchmark
                // eligible; manual review is required before they may be used.
                var currency = ResolveBusinessCurrency(row.CurrencyReference, row.Currency);
                var hasValidFromSource = row.ValidFrom.HasValue || workItem.FallbackFrom.HasValue;
                var hasValidToSource = row.ValidTo.HasValue || workItem.FallbackTo.HasValue;
                var needsValidityReview =
                    !hasValidFromSource || !hasValidToSource || validTo < validFrom;
                var needsCurrencyReview = string.IsNullOrWhiteSpace(currency);
                var metadata = ReadMultiContainerMetadata(row.RawJson);
                var requiresManualReview = needsValidityReview || needsCurrencyReview
                    || metadata.RequiresManualReview;
                var provisionalFrom = validFrom ?? DateTime.UtcNow.Date;
                var provisionalTo = validTo >= provisionalFrom
                    ? validTo.Value
                    : provisionalFrom;

                var basis = ResolveRateBasis(workItem.ShipmentMode);
                var originalAmount = row.TotalSale ?? SumRawComponents(row);
                // Zero confidence explicitly gates the observation out of Average.
                // ApplyManualReview promotes it to confidence 1 once validated.
                var extractionConfidence = requiresManualReview
                    ? 0m
                    : ResolveExtractionConfidence(row.Status);

                var observation = CompetitorRateObservation.Create(
                    workItem.CompetitorTariffId,
                    null,
                    workItem.CompetitorCompanyName,
                    null,
                    row.Id,
                    workItem.ShipmentMode,
                    currency ?? "UNSPECIFIED",
                    provisionalFrom,
                    provisionalTo,
                    basis,
                    row.OceanFreight,
                    row.OriginCharges,
                    row.DestinationCharges,
                    null,
                    row.Surcharges,
                    originalAmount,
                    extractionConfidence,
                    JsonSerializer.Serialize(new
                    {
                        row.Id,
                        row.SourceSheetName,
                        row.SourceRowNumber,
                        row.Status,
                        row.RawJson,
                        RequiresManualReview = requiresManualReview,
                        SourceContainerQuantity = metadata.Quantity,
                        SourceUnitPricesNormalized = metadata.UnitPricesNormalized,
                        MissingValidity = needsValidityReview,
                        MissingCurrency = needsCurrencyReview,
                    })
                );

                // An unknown currency cannot be converted safely; retain the raw
                // observation and let the reviewer supply its real currency.
                if (!needsCurrencyReview)
                {
                    try
                    {
                        var normalized = await normalizationService.NormalizeAsync(
                        new MarketRateNormalizationRequest(
                            workItem.IncotermId,
                            null,
                            row.OriginPortReference?.Id,
                            row.OriginPort,
                            row.PortOfExitReference?.Id,
                            row.PortOfExit,
                            row.DestinationPortReference?.Id,
                            row.DestinationPort,
                            row.CarrierReference?.Id,
                            row.Carrier,
                            row.ContainerTypeReference?.Id,
                            row.ContainerType,
                            workItem.ShipmentMode.ToString(),
                            workItem.ShipmentMode,
                            currency ?? "UNSPECIFIED",
                            metadata.Quantity,
                            basis,
                            null,
                            row.OceanFreight,
                            row.OriginCharges,
                            row.DestinationCharges,
                            null,
                            row.Surcharges,
                            row.TotalSale,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            requiresManualReview ? null : validFrom
                        ),
                        cancellationToken
                    );

                    var normalizedAmount = normalized.NormalizedAllIn
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
                        normalized.Route.Pod.Id,
                        normalized.Route.Pod.Name,
                        normalized.Route.Pod.Code,
                        normalized.Carrier.Id,
                        normalized.Carrier.Name,
                        normalized.Carrier.Code,
                        normalized.Equipment.Id,
                        normalized.Equipment.Code,
                        metadata.Quantity,
                        normalized.NormalizedOceanFreight,
                        normalized.NormalizedAllIn,
                        normalized.Currency.NormalizedCurrency,
                        normalizedAmount,
                        normalized.Currency.RateToUsd,
                        normalized.Currency.ExchangeRateDate,
                        normalized.NormalizationConfidence
                    );
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        // A malformed competitor row must not abort the entire document.
                        // The original observation remains available for manual review.
                        logger.LogWarning(
                            exception,
                            "Competitor tariff {TariffId} row {RowId} needs normalization review.",
                            workItem.CompetitorTariffId,
                            row.Id
                        );
                    }
                }

                if (!IsUsableObservation(observation))
                    reviewCount++;

                dbContext.CompetitorRateObservations.Add(observation);
                observations.Add(observation);
            }

            reviewCount = Math.Max(reviewCount, extraction.Summary.InvalidRows);

            var aggregateFrom = observations.Count > 0
                ? observations.Min(x => x.ValidFrom)
                : entity.ValidFrom;
            var aggregateTo = observations.Count > 0
                ? observations.Max(x => x.ValidTo)
                : entity.ValidTo;

            var status = observations.Count == 0 || reviewCount > 0
                ? "ReviewRequired"
                : "Processed";

            entity.CompleteImport(
                observations.Where(x => x.PolId.HasValue).Select(x => x.PolId!.Value).ToArray(),
                observations.Where(x => x.PoeId.HasValue).Select(x => x.PoeId!.Value).ToArray(),
                observations.Where(x => x.PodId.HasValue).Select(x => x.PodId!.Value).ToArray(),
                observations.Where(x => x.CarrierId.HasValue).Select(x => x.CarrierId!.Value).ToArray(),
                aggregateFrom,
                aggregateTo,
                extraction.ExtractionExecutionId,
                observations.Count,
                reviewCount,
                status
            );

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CompetitorExtractionException)
        {
            // Retry and error persistence are owned by the durable worker.
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to process competitor tariff {CompetitorTariffId}.",
                workItem.CompetitorTariffId
            );

            dbContext.ChangeTracker.Clear();

            var failedEntity = await dbContext.CompetitorTariffs
                .FirstOrDefaultAsync(
                    x => x.Id == workItem.CompetitorTariffId,
                    CancellationToken.None
                );

            if (failedEntity is not null)
            {
                failedEntity.MarkImportFailed(null, 1);
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }

            throw;
        }
    }

    private static bool IsUsableObservation(CompetitorRateObservation observation)
    {
        var normalizedAmount = observation.NormalizedAllIn
            ?? observation.NormalizedAmount
            ?? observation.NormalizedOceanFreight;

        if (
            observation.ExtractionConfidence <= 0m
            || !observation.IncotermId.HasValue
            || !observation.PolId.HasValue
            || !normalizedAmount.HasValue
            || !string.Equals(
                observation.NormalizedCurrency,
                "USD",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return false;
        }

        return observation.Mode is not (ShipmentMode.Fcl or ShipmentMode.Ftl)
            || observation.ContainerTypeId.HasValue;
    }

    private static MultiContainerMetadata ReadMultiContainerMetadata(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return new MultiContainerMetadata(1, false, false);

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new MultiContainerMetadata(1, false, true);

            // Single-container competitor PDFs can flag implausible published rates
            // or missing source fields. Honour that flag even when the source is
            // not a MultiContainerQuotation and never include it in Average before
            // human approval.
            var explicitReview = root.TryGetProperty("RequiresManualReview", out var sourceReview)
                && string.Equals(sourceReview.ToString(), "true", StringComparison.OrdinalIgnoreCase);
            if (!root.TryGetProperty("MultiContainerQuotation", out var marker)
                || !string.Equals(marker.ToString(), "true", StringComparison.OrdinalIgnoreCase))
                return new MultiContainerMetadata(1, false, explicitReview);

            var quantity = 0;
            if (root.TryGetProperty("Quantity", out var rawQuantity))
                int.TryParse(rawQuantity.ToString(), out quantity);

            var unitPricesNormalized = root.TryGetProperty("UnitPricesNormalized", out var unit)
                && string.Equals(unit.ToString(), "true", StringComparison.OrdinalIgnoreCase);
            var needsReview = explicitReview;

            // Fail closed: invoice totals must never become single-container prices.
            if (quantity < 2 || quantity > 100 || !unitPricesNormalized)
                return new MultiContainerMetadata(1, false, true);

            return new MultiContainerMetadata(quantity, true, needsReview);
        }
        catch (JsonException)
        {
            return new MultiContainerMetadata(1, false, true);
        }
    }

    private sealed record MultiContainerMetadata(
        int Quantity,
        bool UnitPricesNormalized,
        bool RequiresManualReview
    );

    private static MarketRateBasis ResolveRateBasis(ShipmentMode mode) =>
        mode switch
        {
            ShipmentMode.Fcl => MarketRateBasis.PerContainer,
            ShipmentMode.Ftl => MarketRateBasis.PerTruck,
            ShipmentMode.Lcl => MarketRateBasis.WeightOrMeasure,
            ShipmentMode.Ltl => MarketRateBasis.PerShipment,
            ShipmentMode.Air or ShipmentMode.AirConsol => MarketRateBasis.PerKg,
            _ => MarketRateBasis.Unknown,
        };

    // Competitor PDFs may express business currency as ISO code, symbol, or localized label.
    private static string? ResolveBusinessCurrency(
        DataExtractionCatalogReference? reference,
        string? fallback
    )
    {
        var candidates = new[]
        {
            reference?.RawValue,
            reference?.Name,
            reference?.Slug,
            reference?.Code,
            fallback,
        };

        foreach (var candidate in candidates)
        {
            var value = candidate?.Trim();
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (
                value.Contains((char)36)
                || value.Equals("USD", StringComparison.OrdinalIgnoreCase)
                || value.Contains("DOLAR", StringComparison.OrdinalIgnoreCase)
            )
            {
                return "USD";
            }

            if (
                value.Contains((char)8353)
                || value.Equals("CRC", StringComparison.OrdinalIgnoreCase)
                || value.Contains("COLON", StringComparison.OrdinalIgnoreCase)
            )
            {
                return "CRC";
            }

            if (
                value.Contains((char)8364)
                || value.Equals("EUR", StringComparison.OrdinalIgnoreCase)
                || value.Contains("EURO", StringComparison.OrdinalIgnoreCase)
            )
            {
                return "EUR";
            }

            if (
                value.Contains((char)163)
                || value.Equals("GBP", StringComparison.OrdinalIgnoreCase)
            )
            {
                return "GBP";
            }

            if (value.Length == 3 && value.All(char.IsLetter))
                return value.ToUpperInvariant();
        }

        return string.IsNullOrWhiteSpace(fallback)
            ? null
            : fallback.Trim().ToUpperInvariant();
    }

    private static decimal ResolveExtractionConfidence(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "valid" or "approved" => 1m,
            "warning" or "review" or "pending" => 0.85m,
            _ => 0.65m,
        };

    private static decimal? SumRawComponents(DataExtractionFclPricingRow row)
    {
        var values = new[]
        {
            row.OceanFreight,
            row.OriginCharges,
            row.DestinationCharges,
            row.Surcharges,
        };

        return values.Any(x => x.HasValue)
            ? values.Where(x => x.HasValue).Sum(x => x!.Value)
            : null;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
