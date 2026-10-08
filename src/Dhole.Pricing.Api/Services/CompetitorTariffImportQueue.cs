using System.Text.Json;
using System.Threading.Channels;
using Dhole.Pricing.Application.Abstractions.MarketPricing;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.MarketPricing.Normalization;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Domain.MarketPricing.Enums;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Services;

public sealed record CompetitorTariffImportWorkItem(
    Guid CompetitorTariffId,
    Guid IncotermId,
    string CompetitorCompanyName,
    ShipmentMode ShipmentMode,
    DateTime? FallbackFrom,
    DateTime? FallbackTo,
    string OriginalFileName,
    string? ContentType,
    string? FileExtension,
    long FileSizeBytes,
    string FileHash,
    Guid? RequestedBy,
    string? RequestedByName,
    byte[] FileContent
);

public interface ICompetitorTariffImportQueue
{
    ValueTask QueueAsync(
        CompetitorTariffImportWorkItem workItem,
        CancellationToken cancellationToken = default
    );

    ValueTask<CompetitorTariffImportWorkItem> DequeueAsync(
        CancellationToken cancellationToken
    );
}

public sealed class CompetitorTariffImportQueue : ICompetitorTariffImportQueue
{
    private readonly Channel<CompetitorTariffImportWorkItem> _channel =
        Channel.CreateUnbounded<CompetitorTariffImportWorkItem>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            }
        );

    public ValueTask QueueAsync(
        CompetitorTariffImportWorkItem workItem,
        CancellationToken cancellationToken = default
    ) => _channel.Writer.WriteAsync(workItem, cancellationToken);

    public ValueTask<CompetitorTariffImportWorkItem> DequeueAsync(
        CancellationToken cancellationToken
    ) => _channel.Reader.ReadAsync(cancellationToken);
}

public sealed class CompetitorTariffImportWorker(
    ICompetitorTariffImportQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<CompetitorTariffImportWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            CompetitorTariffImportWorkItem workItem;

            try
            {
                workItem = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider
                    .GetRequiredService<CompetitorTariffImportProcessor>();

                await processor.ProcessAsync(workItem, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Competitor tariff import {CompetitorTariffId} failed in background worker.",
                    workItem.CompetitorTariffId
                );
            }
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
                    null,
                    workItem.RequestedBy,
                    workItem.RequestedByName,
                    workItem.FileContent
                ),
                cancellationToken
            );

            if (!extraction.Success)
            {
                entity.MarkImportFailed(
                    extraction.ExtractionExecutionId,
                    Math.Max(1, extraction.Summary.InvalidRows)
                );
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
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
                var needsValidityReview =
                    !validFrom.HasValue || !validTo.HasValue || validTo < validFrom;
                var needsCurrencyReview = string.IsNullOrWhiteSpace(currency);
                var requiresManualReview = needsValidityReview || needsCurrencyReview;
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
                        MissingValidity = needsValidityReview,
                        MissingCurrency = needsCurrencyReview,
                    })
                );

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
                        1,
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
                    1,
                    normalized.NormalizedOceanFreight,
                    normalized.NormalizedAllIn,
                    normalized.Currency.NormalizedCurrency,
                    normalizedAmount,
                    normalized.Currency.RateToUsd,
                    normalized.Currency.ExchangeRateDate,
                    normalized.NormalizationConfidence
                );

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
