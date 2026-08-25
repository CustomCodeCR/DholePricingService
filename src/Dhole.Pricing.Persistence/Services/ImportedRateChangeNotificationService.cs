using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Imports.Entities;
using Dhole.Pricing.Domain.Imports.Enums;
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

        if (currentRate.SourceType != ImportSourceType.Email || currentRate.Status != ImportStatus.Pending)
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

        var subject = "Nuevas tarifas de correo pendientes de aprobación";
        var body =
            $"Hay nuevas tarifas recibidas por correo pendientes de revisión. "
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

        var amount = currentRate.OceanFreight ?? currentRate.Freight;
        if (amount <= 0m) return;

        var previous = await dbContext.ImportFclRates
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted
                && x.UsedAsRateCount > 0
                && x.ImportBatchId != currentRate.ImportBatchId
                && x.CreatedAtUtc < currentRate.CreatedAtUtc
                && x.CarrierId == currentRate.CarrierId
                && x.PolId == currentRate.PolId
                && x.PoeId == currentRate.PoeId
                && x.ContainerTypeId == currentRate.ContainerTypeId
            )
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (previous is null) return;

        var previousAmount = previous.OceanFreight ?? previous.Freight;
        if (previousAmount <= 0m || previousAmount == amount) return;

        var direction = amount < previousAmount ? "bajó" : "subió";
        var delta = amount - previousAmount;
        var percentage = previousAmount == 0m
            ? 0m
            : decimal.Round((delta / previousAmount) * 100m, 2);
        var subject = $"Tarifa importada {direction}: {currentRate.CarrierName} {currentRate.PolName} → {currentRate.PoeName}";
        var body =
            $"La tarifa importada {direction} respecto a la última tarifa importada utilizada comparable. "
            + $"Naviera: {currentRate.CarrierName}; POL: {currentRate.PolName}; POE: {currentRate.PoeName}; "
            + $"contenedor: {currentRate.ContainerTypeName}; anterior: {previous.CurrencyCode} {previousAmount:N2}; "
            + $"nueva: {currentRate.CurrencyCode} {amount:N2}; variación: {delta:+0.00;-0.00;0.00} ({percentage:+0.00;-0.00;0.00}%). "
            + "Revise la importación para crear una nueva tarifa comercial si corresponde.";

        var payload = new
        {
            type = "pricing.imported-rate.variation",
            currentImportRateId = currentRate.Id,
            previousImportRateId = previous.Id,
            currentRate.ImportBatchId,
            currentRate.CarrierName,
            currentRate.PolName,
            currentRate.PoeName,
            currentRate.ContainerTypeName,
            previousCurrencyCode = previous.CurrencyCode,
            currentCurrencyCode = currentRate.CurrencyCode,
            previousAmount,
            previousUsedAsRateCount = previous.UsedAsRateCount,
            previousCreatedAsRateHeaderId = previous.CreatedAsRateHeaderId,
            currentAmount = amount,
            delta,
            percentage,
            direction,
            action = "create-rate-from-import",
        };

        IReadOnlyCollection<PricingNotificationRecipient> recipients;
        try
        {
            recipients = await recipientProvider.GetPricingRecipientsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "No se pudieron resolver usuarios de Pricing para la variación de tarifa {ImportRateId}.",
                currentRate.Id
            );
            return;
        }

        foreach (var recipient in recipients)
        {
            if (recipient.UserId != Guid.Empty)
            {
                await QueueAsync(
                    "System",
                    "pricing.imported-rate.variation",
                    recipient,
                    subject,
                    body,
                    payload,
                    currentRate.Id,
                    $"pricing-rate-change:{currentRate.Id:N}:system:{recipient.UserId:N}",
                    cancellationToken
                );
            }

            if (!string.IsNullOrWhiteSpace(recipient.Email))
            {
                await QueueAsync(
                    "Email",
                    "pricing.imported-rate.variation",
                    recipient,
                    subject,
                    body,
                    payload,
                    currentRate.Id,
                    $"pricing-rate-change:{currentRate.Id:N}:email:{recipient.Email}",
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
}
