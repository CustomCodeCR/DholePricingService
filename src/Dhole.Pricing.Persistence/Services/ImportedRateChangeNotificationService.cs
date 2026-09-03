using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;
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
    IConfiguration configuration,
    ILogger<ImportedRateChangeNotificationService> logger
) : IImportedRateChangeNotificationService
{
    private const string EventName = "notifications.notification.requested";

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

        var completeCost = ResolveCompleteImportedCost(currentRate);
        if (completeCost <= 0m) return;

        var comparable = dbContext.RateHeaders
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted
                && x.PolId == currentRate.PolId
                && x.PoeId == currentRate.PoeId
                && x.ContainerTypeId == currentRate.ContainerTypeId
                && x.TotalCostAmount > 0m);

        var sent = await comparable
            .Where(x => x.Status == RateStatus.Sent)
            .OrderBy(x => x.TotalCostAmount)
            .FirstOrDefaultAsync(cancellationToken);

        var acceptedCutoff = DateTime.UtcNow.AddDays(-7);
        var accepted = await comparable
            .Where(x =>
                x.Status == RateStatus.AcceptedByClient
                && (x.UpdatedAtUtc ?? x.CreatedAtUtc) <= acceptedCutoff)
            .OrderBy(x => x.TotalCostAmount)
            .FirstOrDefaultAsync(cancellationToken);

        IReadOnlyCollection<PricingNotificationRecipient> recipients;
        try
        {
            recipients = await recipientProvider.GetPricingRecipientsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "No se pudieron resolver usuarios de Pricing para comparar la tarifa completa {ImportRateId}.",
                currentRate.Id
            );
            return;
        }

        if (recipients.Count == 0) return;

        if (sent is not null && completeCost < sent.TotalCostAmount)
        {
            await QueueLowerCompleteRateAsync(
                currentRate,
                completeCost,
                sent.Id,
                sent.RateCode,
                sent.Status.ToString(),
                sent.TotalCostAmount,
                recipients,
                "sent",
                cancellationToken);
        }

        if (accepted is not null && completeCost < accepted.TotalCostAmount)
        {
            await QueueLowerCompleteRateAsync(
                currentRate,
                completeCost,
                accepted.Id,
                accepted.RateCode,
                accepted.Status.ToString(),
                accepted.TotalCostAmount,
                recipients,
                "accepted-7d",
                cancellationToken);
        }
    }

    private async Task QueueLowerCompleteRateAsync(
        ImportFclRates currentRate,
        decimal completeCost,
        Guid comparedRateId,
        string comparedRateCode,
        string comparedStatus,
        decimal comparedCost,
        IReadOnlyCollection<PricingNotificationRecipient> recipients,
        string comparisonKind,
        CancellationToken cancellationToken)
    {
        var savings = comparedCost - completeCost;
        var percentage = comparedCost <= 0m ? 0m : decimal.Round((savings / comparedCost) * 100m, 2);
        var statusLabel = comparisonKind == "sent" ? "enviada" : "aceptada hace al menos 7 días";
        var subject = $"Tarifa completa más baja: {currentRate.PolName} → {currentRate.PoeName} · {currentRate.ContainerTypeName}";
        var body =
            $"Se registró una tarifa cuyo costo completo es menor que una tarifa {statusLabel}. "
            + $"Ruta: {currentRate.PolName} → {currentRate.PoeName} → {currentRate.PodName}; equipo: {currentRate.ContainerTypeName}. "
            + $"Tarifa comparada {comparedRateCode}: {currentRate.CurrencyCode} {comparedCost:N2}; "
            + $"nueva tarifa completa: {currentRate.CurrencyCode} {completeCost:N2}; ahorro: {savings:N2} ({percentage:N2}%). "
            + "La comparación considera el costo completo (flete, origen, destino y recargos), no solamente el flete internacional.";

        var payload = new
        {
            type = "pricing.imported-rate.lower-complete-rate",
            comparisonKind,
            currentImportRateId = currentRate.Id,
            currentRate.ImportBatchId,
            comparedRateId,
            comparedRateCode,
            comparedStatus,
            currentRate.PolName,
            currentRate.PoeName,
            currentRate.PodName,
            currentRate.ContainerTypeName,
            currencyCode = currentRate.CurrencyCode,
            comparedCompleteCost = comparedCost,
            newCompleteCost = completeCost,
            savings,
            percentage,
            action = "review-lower-complete-rate",
            route = "/pricing/imports",
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
                    $"pricing-lower-complete:{currentRate.Id:N}:{comparisonKind}:system:{recipient.UserId:N}",
                    cancellationToken);
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
                    $"pricing-lower-complete:{currentRate.Id:N}:{comparisonKind}:email:{recipient.Email}",
                    cancellationToken);
            }
        }
    }

    private static decimal ResolveCompleteImportedCost(ImportFclRates rate)
    {
        if (rate.TotalCost is > 0m) return rate.TotalCost.Value;

        return Math.Max(0m, rate.OceanFreight ?? rate.Freight)
            + Math.Max(0m, rate.OriginCharges ?? 0m)
            + Math.Max(0m, rate.DestinationCharges ?? 0m)
            + Math.Max(0m, rate.Surcharges ?? 0m);
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
}
