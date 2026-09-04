using System.Text.Json;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Features.Rates.CreateRate;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dhole.Pricing.Persistence.Services;

public sealed class ImportedRateChangeNotificationService(
    ServiceDbContext dbContext,
    IIntegrationEventOutboxWriter outbox,
    IPricingNotificationRecipientProvider recipientProvider,
    IRateFixedCostSynchronizer fixedCostSynchronizer,
    IConfiguration configuration,
    ILogger<ImportedRateChangeNotificationService> logger
) : IImportedRateChangeNotificationService
{
    private const string EventName = "notifications.notification.requested";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task QueueApprovalRequiredNotificationsAsync(
        ImportFclRates currentRate,
        CancellationToken cancellationToken = default
    )
    {
        if (!configuration.GetValue("Pricing:ImportApprovalNotifications:Enabled", true))
            return;

        if (currentRate.SourceType != ImportSourceType.Email || currentRate.Status is not (ImportStatus.Pending or ImportStatus.PreAuthorized))
            return;

        // Notify once per imported email batch. Subsequent rows in the same batch see the
        // first persisted row and do not enqueue another email/system notification.
        var batchAlreadyQueued = await dbContext.ImportFclRates
            .AsNoTracking()
            .AnyAsync(
                x => !x.IsDeleted
                    && x.ImportBatchId == currentRate.ImportBatchId
                    && x.Id != currentRate.Id,
                cancellationToken
            );

        if (batchAlreadyQueued)
            return;

        IReadOnlyCollection<PricingNotificationRecipient> recipients;
        try
        {
            recipients = await recipientProvider.GetPricingRecipientsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "No se pudieron resolver aprobadores de Pricing para el lote {ImportBatchId}.",
                currentRate.ImportBatchId
            );
            return;
        }

        if (recipients.Count == 0)
        {
            logger.LogWarning(
                "No existen usuarios con el scope de revisión de tarifas para el lote {ImportBatchId}.",
                currentRate.ImportBatchId
            );
            return;
        }

        var subject = "Nuevas tarifas preautorizadas pendientes de preaprobación";
        var body =
            $"Hay nuevas tarifas recibidas por correo que ya pasaron la preautorización automática y están pendientes de preaprobación. "
            + $"Naviera: {currentRate.CarrierName}; ruta: {currentRate.PolName} → {currentRate.PoeName} → {currentRate.PodName}; "
            + $"equipo: {currentRate.ContainerTypeName}. Abra Pricing > Tarifas recibidas para revisarlas y aprobarlas o rechazarlas.";

        var payload = new
        {
            type = "pricing.imported-rate.approval-required",
            currentRate.ImportBatchId,
            firstImportRateId = currentRate.Id,
            sourceType = currentRate.SourceType.ToString(),
            currentRate.CarrierName,
            currentRate.PolName,
            currentRate.PoeName,
            currentRate.PodName,
            currentRate.ContainerTypeName,
            action = "review-imported-rates",
            route = "/pricing/imports",
        };

        foreach (var recipient in recipients)
        {
            if (recipient.UserId != Guid.Empty)
            {
                await QueueAsync(
                    "System",
                    "pricing.imported-rate.approval-required",
                    recipient,
                    subject,
                    body,
                    payload,
                    currentRate.Id,
                    $"pricing-approval:{currentRate.ImportBatchId:N}:system:{recipient.UserId:N}",
                    cancellationToken
                );
            }

            if (!string.IsNullOrWhiteSpace(recipient.Email))
            {
                await QueueAsync(
                    "Email",
                    "pricing.imported-rate.approval-required",
                    recipient,
                    subject,
                    body,
                    payload,
                    currentRate.Id,
                    $"pricing-approval:{currentRate.ImportBatchId:N}:email:{recipient.Email}",
                    cancellationToken
                );
            }
        }
    }

    public async Task QueueVariationNotificationsAsync(
        ImportFclRates currentRate,
        CancellationToken cancellationToken = default
    )
    {
        if (!configuration.GetValue("Pricing:RateChangeNotifications:Enabled", true))
            return;

        var acceptedCutoffUtc = DateTime.UtcNow.AddDays(-7);

        var baselines = await dbContext.RateHeaders
            .AsNoTracking()
            .Include(x => x.RateDetails)
            .Include(x => x.RateServices)
            .Include(x => x.RateContainers)
            .Where(x =>
                !x.IsDeleted
                && x.PolId == currentRate.PolId
                && x.PoeId == currentRate.PoeId
                && x.ContainerTypeId == currentRate.ContainerTypeId
                && x.ShipmentMode == ShipmentMode.Fcl
                && x.RateContainers.All(c => c.ContainerTypeId == currentRate.ContainerTypeId)
                && (
                    (x.Status == RateStatus.Sent && x.TotalSaleAmount > 0m)
                    || (
                        x.Status == RateStatus.AcceptedByClient
                        && x.TotalCostAmount > 0m
                        && (x.UpdatedAtUtc ?? x.CreatedAtUtc) >= acceptedCutoffUtc
                    )
                )
            )
            .OrderByDescending(x => x.UpdatedAtUtc ?? x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (baselines.Count == 0)
            return;

        var existingKeys = await dbContext.RateComparisons
            .AsNoTracking()
            .Where(x => x.SourceImportFclRateId == currentRate.Id)
            .Select(x => new { x.ComparedRateHeaderId, x.ComparisonType })
            .ToListAsync(cancellationToken);

        var existing = existingKeys
            .Select(x => $"{x.ComparedRateHeaderId:N}:{(int)x.ComparisonType}")
            .ToHashSet(StringComparer.Ordinal);

        var comparisons = new List<RateComparison>();

        foreach (var baseline in baselines)
        {
            var comparisonType = baseline.Status == RateStatus.Sent
                ? RateComparisonType.Sent
                : RateComparisonType.AcceptedRecent;

            if (existing.Contains($"{baseline.Id:N}:{(int)comparisonType}"))
                continue;

            try
            {
                var candidateCommand = BuildCandidateCommand(currentRate, baseline);
                var candidate = await BuildCompleteCandidateAsync(
                    currentRate,
                    baseline,
                    candidateCommand,
                    cancellationToken
                );

                var baselineComparedAmount = comparisonType == RateComparisonType.Sent
                    ? baseline.TotalSaleAmount
                    : baseline.TotalCostAmount;
                var candidateComparedAmount = comparisonType == RateComparisonType.Sent
                    ? candidate.TotalSaleAmount
                    : candidate.TotalCostAmount;

                if (candidateComparedAmount <= 0m || candidateComparedAmount >= baselineComparedAmount)
                    continue;

                var comparison = RateComparison.Create(
                    currentRate.Id,
                    baseline.Id,
                    baseline.RateCode,
                    comparisonType,
                    currentRate.PolId,
                    currentRate.PolName,
                    currentRate.PoeId,
                    currentRate.PoeName,
                    currentRate.ContainerTypeId,
                    currentRate.ContainerTypeName,
                    baseline.CurrencyCode,
                    baseline.TotalCostAmount,
                    baseline.TotalSaleAmount,
                    candidate.TotalCostAmount,
                    candidate.TotalSaleAmount,
                    baselineComparedAmount,
                    candidateComparedAmount,
                    JsonSerializer.Serialize(candidateCommand, JsonOptions)
                );

                AddComparisonDetails(comparison, baseline, candidate);
                dbContext.RateComparisons.Add(comparison);
                comparisons.Add(comparison);
            }
            catch (Exception exception)
            {
                // La detección de una oportunidad nunca debe impedir registrar la nueva tarifa importada.
                logger.LogWarning(
                    exception,
                    "No se pudo construir la tarifa automática para comparar importada {ImportRateId} contra {RateId}.",
                    currentRate.Id,
                    baseline.Id
                );
            }
        }

        if (comparisons.Count == 0)
            return;

        IReadOnlyCollection<PricingNotificationRecipient> recipients;
        try
        {
            recipients = await recipientProvider.GetPricingRecipientsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Se detectaron tarifas más bajas para {ImportRateId}, pero no se pudieron resolver destinatarios de Pricing.",
                currentRate.Id
            );
            return;
        }

        if (recipients.Count == 0)
            return;

        foreach (var comparison in comparisons)
        {
            await QueueLowerCompleteRateAsync(
                currentRate,
                comparison,
                recipients,
                cancellationToken
            );
        }
    }

    private CreateRateCommand BuildCandidateCommand(ImportFclRates currentRate, RateHeader baseline)
    {
        var manualDetails = baseline.RateDetails
            .Where(x =>
                x.CostDetailType != CostDetailType.Freight
                && !(x.CostType == CostType.Fixed && x.CostId.HasValue)
            )
            .Select(x => new CreateRateDetailCommandItem(
                x.CostId,
                x.Name,
                x.CostDetailType,
                x.CostType,
                x.CurrencyId,
                x.CurrencyName,
                x.CurrencyCode,
                x.CostAmount,
                x.SaleAmount,
                x.Notes,
                x.Quantity,
                x.ChargeBasis,
                x.ApplyDestinationTax,
                x.DestinationTaxRate
            ))
            .ToArray();

        var services = baseline.RateServices
            .Select(x => new RateServiceSelection(x.ServiceId, x.ServiceName, x.ServiceCode))
            .ToArray();

        var quantity = Math.Max(baseline.ContainerQuantity, 1);
        var transitTime = currentRate.TransitDays.HasValue
            ? $"{currentRate.TransitDays.Value} días"
            : baseline.TransitTime;

        return new CreateRateCommand(
            SourceImportFclRateId: currentRate.Id,
            AgentId: currentRate.AgentId,
            AgentName: currentRate.AgentName,
            AgentCode: currentRate.AgentCode,
            CarrierId: currentRate.CarrierId,
            CarrierName: currentRate.CarrierName,
            CarrierCode: currentRate.CarrierCode,
            PolId: currentRate.PolId,
            PolName: currentRate.PolName,
            PolCode: currentRate.PolCode,
            PoeId: currentRate.PoeId,
            PoeName: currentRate.PoeName,
            PoeCode: currentRate.PoeCode,
            PodId: currentRate.PodId,
            PodName: currentRate.PodName,
            PodCode: currentRate.PodCode,
            ContainerTypeId: currentRate.ContainerTypeId,
            ContainerTypeName: currentRate.ContainerTypeName,
            ContainerTypeCode: currentRate.ContainerTypeCode,
            IncotermId: baseline.IncotermId,
            IncotermName: baseline.IncotermName,
            IncotermCode: baseline.IncotermCode,
            CurrencyId: baseline.CurrencyId,
            CurrencyName: baseline.CurrencyName,
            CurrencyCode: baseline.CurrencyCode,
            FreeDays: currentRate.FreeDays,
            ValidFrom: currentRate.ValidFrom,
            ValidTo: currentRate.ValidTo,
            ContainerQuantity: quantity,
            ClientName: baseline.ClientName,
            ExecutiveName: baseline.ExecutiveName,
            IdtraNumber: null,
            QuoNumber: null,
            Includes: baseline.Includes,
            SubjectTo: baseline.SubjectTo,
            Excludes: baseline.Excludes,
            TransitTime: transitTime,
            Details: manualDetails,
            Containers:
            [
                new RateContainerCommandItem(
                    currentRate.ContainerTypeId,
                    currentRate.ContainerTypeName,
                    currentRate.ContainerTypeCode,
                    quantity
                ),
            ],
            RateType: baseline.RateType,
            ShipmentMode: ShipmentMode.Fcl,
            KgPerCbm: baseline.KgPerCbm,
            TotalPackages: baseline.TotalPackages,
            TotalPallets: baseline.TotalPallets,
            TotalWeightKg: baseline.TotalWeightKg,
            TotalVolumeCbm: baseline.TotalVolumeCbm,
            CargoLines: [],
            PickupAddress: baseline.PickupAddress,
            PickupLatitude: baseline.PickupLatitude,
            PickupLongitude: baseline.PickupLongitude,
            ExchangeRatePurchase: baseline.ExchangeRatePurchase,
            ExchangeRateSale: baseline.ExchangeRateSale,
            ExchangeRateApplied: baseline.ExchangeRateApplied,
            CanApproveImportedRate: true,
            OperationType: baseline.OperationType,
            Services: services,
            CanApproveLowMargin: true,
            CreatedBy: null
        );
    }

    private async Task<RateHeader> BuildCompleteCandidateAsync(
        ImportFclRates currentRate,
        RateHeader baseline,
        CreateRateCommand command,
        CancellationToken cancellationToken
    )
    {
        var candidate = RateHeader.Create(
            $"CMP-{Guid.NewGuid():N}",
            currentRate.Id,
            currentRate.AgentId,
            currentRate.AgentName,
            currentRate.AgentCode,
            currentRate.CarrierId,
            currentRate.CarrierName,
            currentRate.CarrierCode,
            currentRate.PolId,
            currentRate.PolName,
            currentRate.PolCode,
            currentRate.PoeId,
            currentRate.PoeName,
            currentRate.PoeCode,
            currentRate.PodId,
            currentRate.PodName,
            currentRate.PodCode,
            currentRate.ContainerTypeId,
            currentRate.ContainerTypeName,
            currentRate.ContainerTypeCode,
            baseline.IncotermId,
            baseline.IncotermName,
            baseline.IncotermCode,
            baseline.CurrencyId,
            baseline.CurrencyName,
            baseline.CurrencyCode,
            currentRate.FreeDays,
            currentRate.ValidFrom,
            currentRate.ValidTo,
            Math.Max(baseline.ContainerQuantity, 1),
            baseline.ClientName,
            idtraNumber: null,
            quoNumber: null,
            baseline.Includes,
            baseline.SubjectTo,
            baseline.Excludes,
            command.TransitTime,
            baseline.RateType,
            createdBy: null
        );

        candidate.ConfigurePickupLocation(
            baseline.PickupAddress,
            baseline.PickupLatitude,
            baseline.PickupLongitude
        );
        candidate.ConfigureExecutive(baseline.ExecutiveName);
        candidate.SetOperationType(baseline.OperationType);
        candidate.ConfigureServices(
            baseline.RateServices
                .Select(x => new RateServiceSelection(x.ServiceId, x.ServiceName, x.ServiceCode))
                .ToArray()
        );

        var appliedExchangeRate = baseline.ExchangeRateApplied ?? baseline.ExchangeRateSale;
        if (appliedExchangeRate is > 0m)
        {
            candidate.ConfigureExchangeRateSnapshot(
                baseline.ExchangeRatePurchase,
                baseline.ExchangeRateSale,
                appliedExchangeRate.Value,
                baseline.ExchangeRateDate,
                baseline.ExchangeRateCapturedAtUtc ?? DateTime.UtcNow,
                baseline.ExchangeRateSource ?? "Comparación",
                baseline.ExchangeRateManualOverride,
                updatedBy: null
            );
        }

        candidate.ConfigureShipment(
            ShipmentMode.Fcl,
            baseline.TotalPackages,
            baseline.TotalPallets,
            baseline.TotalWeightKg,
            baseline.TotalVolumeCbm,
            baseline.KgPerCbm,
            baseline.CargoLinesJson,
            updatedBy: null
        );

        var freightCost = Math.Max(0m, currentRate.OceanFreight ?? currentRate.Freight);
        var freightSale = Math.Max(0m, currentRate.TotalSale ?? currentRate.OceanFreight ?? currentRate.Freight);
        candidate.AddRateDetail(
            candidate.Id,
            costId: null,
            name: "Flete internacional",
            CostDetailType.Freight,
            CostType.Variable,
            ChargeBasis.PerContainer,
            currentRate.CurrencyId,
            currentRate.CurrencyName,
            currentRate.CurrencyCode,
            freightCost,
            freightSale,
            notes: currentRate.SpaceComment,
            quantity: candidate.ContainerQuantity,
            updatedBy: null
        );

        foreach (var detail in command.Details)
        {
            var added = candidate.AddRateDetail(
                candidate.Id,
                detail.CostId,
                detail.Name,
                detail.CostDetailType,
                detail.CostType,
                detail.ChargeBasis ?? ChargeBasis.PerShipment,
                detail.CurrencyId,
                detail.CurrencyName,
                detail.CurrencyCode,
                detail.CostAmount,
                detail.SaleAmount,
                detail.Notes,
                detail.Quantity ?? 1m,
                updatedBy: null
            );
            added.ConfigureDestinationTax(detail.ApplyDestinationTax, detail.DestinationTaxRate);
        }

        await fixedCostSynchronizer.SynchronizeAsync(candidate, updatedBy: null, cancellationToken);
        candidate.SetAmounts(updatedBy: null);
        return candidate;
    }

    private static void AddComparisonDetails(
        RateComparison comparison,
        RateHeader baseline,
        RateHeader candidate
    )
    {
        var baselineLines = AggregateLines(baseline.RateDetails);
        var candidateLines = AggregateLines(candidate.RateDetails);
        var keys = baselineLines.Keys.Union(candidateLines.Keys).OrderBy(x => x.Name).ToArray();

        foreach (var key in keys)
        {
            baselineLines.TryGetValue(key, out var baselineLine);
            candidateLines.TryGetValue(key, out var candidateLine);
            var metadata = candidateLine ?? baselineLine;
            if (metadata is null)
                continue;

            comparison.AddDetail(
                RateComparisonDetail.Create(
                    comparison.Id,
                    key.CostId,
                    key.Name,
                    key.CostDetailType,
                    key.CostType,
                    key.ChargeBasis,
                    key.CurrencyCode,
                    quantity: 1m,
                    baselineCostAmount: baselineLine?.TotalCost ?? 0m,
                    baselineSaleAmount: baselineLine?.TotalSale ?? 0m,
                    candidateCostAmount: candidateLine?.TotalCost ?? 0m,
                    candidateSaleAmount: candidateLine?.TotalSale ?? 0m,
                    notes: metadata.Notes
                )
            );
        }
    }

    private static Dictionary<ComparisonLineKey, ComparisonLine> AggregateLines(
        IReadOnlyCollection<RateDetail> details
    )
    {
        return details
            .GroupBy(x => new ComparisonLineKey(
                x.CostId,
                x.Name.Trim(),
                x.CostDetailType,
                x.CostType,
                x.ChargeBasis,
                x.CurrencyCode.Trim().ToUpperInvariant()
            ))
            .ToDictionary(
                x => x.Key,
                x => new ComparisonLine(
                    x.Sum(d => d.CostAmount * d.Quantity),
                    x.Sum(d => d.SaleAmount * d.Quantity),
                    x.Select(d => d.Notes).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                )
            );
    }

    private async Task QueueLowerCompleteRateAsync(
        ImportFclRates currentRate,
        RateComparison comparison,
        IReadOnlyCollection<PricingNotificationRecipient> recipients,
        CancellationToken cancellationToken
    )
    {
        var isSent = comparison.ComparisonType == RateComparisonType.Sent;
        var statusLabel = isSent ? "enviada" : "aceptada durante los últimos 7 días";
        var comparedMetric = isSent ? "venta completa" : "costo completo";
        var subject = $"Tarifa completa más baja: {currentRate.PolName} → {currentRate.PoeName} · {currentRate.ContainerTypeName}";
        var body =
            $"Se registró una nueva tarifa y Dhole generó una cotización completa que mejora una tarifa {statusLabel}. "
            + $"Ruta: {currentRate.PolName} → {currentRate.PoeName} → {currentRate.PodName}; equipo: {currentRate.ContainerTypeName}. "
            + $"Tarifa {comparison.ComparedRateCode}, {comparedMetric}: {comparison.CurrencyCode} {comparison.BaselineComparedAmount:N2}; "
            + $"nueva tarifa automática: {comparison.CurrencyCode} {comparison.CandidateComparedAmount:N2}; "
            + $"diferencia: {comparison.SavingsAmount:N2} ({comparison.SavingsPercent:N2}%). "
            + "La comparación usa la tarifa completa reconstruida con sus cargos y servicios; no compara únicamente el flete internacional.";

        var payload = new
        {
            type = "pricing.imported-rate.lower-complete-rate",
            comparisonId = comparison.Id,
            comparisonType = comparison.ComparisonType.ToString(),
            currentImportRateId = currentRate.Id,
            currentRate.ImportBatchId,
            comparedRateId = comparison.ComparedRateHeaderId,
            comparedRateCode = comparison.ComparedRateCode,
            currentRate.PolName,
            currentRate.PoeName,
            currentRate.PodName,
            currentRate.ContainerTypeName,
            currencyCode = comparison.CurrencyCode,
            baselineCost = comparison.BaselineCostAmount,
            baselineSale = comparison.BaselineSaleAmount,
            candidateCost = comparison.CandidateCostAmount,
            candidateSale = comparison.CandidateSaleAmount,
            comparedAmount = comparison.BaselineComparedAmount,
            candidateComparedAmount = comparison.CandidateComparedAmount,
            savings = comparison.SavingsAmount,
            percentage = comparison.SavingsPercent,
            action = "review-rate-comparison",
            route = $"/pricing/rate-comparisons/{comparison.Id}",
        };

        foreach (var recipient in recipients)
        {
            if (recipient.UserId != Guid.Empty)
            {
                await QueueAsync(
                    "System",
                    "pricing.imported-rate.lower-complete-rate",
                    recipient,
                    subject,
                    body,
                    payload,
                    currentRate.Id,
                    $"pricing-lower-complete:{currentRate.Id:N}:{comparison.ComparedRateHeaderId:N}:{(int)comparison.ComparisonType}:system:{recipient.UserId:N}",
                    cancellationToken
                );
            }

            if (!string.IsNullOrWhiteSpace(recipient.Email))
            {
                await QueueAsync(
                    "Email",
                    "pricing.imported-rate.lower-complete-rate",
                    recipient,
                    subject,
                    body,
                    payload,
                    currentRate.Id,
                    $"pricing-lower-complete:{currentRate.Id:N}:{comparison.ComparedRateHeaderId:N}:{(int)comparison.ComparisonType}:email:{recipient.Email}",
                    cancellationToken
                );
            }
        }
    }

    private Task QueueAsync(
        string channel,
        string notificationType,
        PricingNotificationRecipient recipient,
        string subject,
        string body,
        object payload,
        Guid importRateId,
        string correlationId,
        CancellationToken cancellationToken
    ) => outbox.WriteAsync(
        EventName,
        EventName,
        BuildRequest(
            channel,
            notificationType,
            recipient.UserId == Guid.Empty ? null : recipient.UserId,
            recipient.Email,
            recipient.DisplayName,
            subject,
            body,
            payload,
            importRateId
        ),
        correlationId: correlationId,
        cancellationToken: cancellationToken
    );

    private static object BuildRequest(
        string channel,
        string notificationType,
        Guid? userId,
        string? email,
        string? displayName,
        string subject,
        string body,
        object payload,
        Guid importRateId
    ) => new
    {
        notificationType,
        templateCode = (string?)null,
        channel,
        entityType = "ImportFclRate",
        entityId = importRateId.ToString(),
        subject,
        body,
        payload,
        maxAttempts = 3,
        recipients = new[]
        {
            new
            {
                userId = userId?.ToString(),
                address = channel == "Email" ? email ?? string.Empty : userId?.ToString() ?? string.Empty,
                displayName,
            },
        },
    };

    private sealed record ComparisonLineKey(
        Guid? CostId,
        string Name,
        CostDetailType CostDetailType,
        CostType CostType,
        ChargeBasis ChargeBasis,
        string CurrencyCode
    );

    private sealed record ComparisonLine(decimal TotalCost, decimal TotalSale, string? Notes);
}
