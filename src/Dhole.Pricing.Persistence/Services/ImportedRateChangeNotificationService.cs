using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Imports.Entities;
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

    public async Task QueueVariationNotificationsAsync(
        ImportFclRates currentRate,
        CancellationToken cancellationToken = default
    )
    {
        if (!configuration.GetValue("Pricing:RateChangeNotifications:Enabled", true))
        {
            return;
        }

        var amount = currentRate.OceanFreight ?? currentRate.Freight;
        if (amount <= 0m) return;

        // La alerta solo es relevante si existe una tarifa importada comparable que ya
        // fue utilizada para crear una tarifa comercial. Una importación meramente
        // aprobada no cuenta como utilizada: CreatedAsRate(...) es quien incrementa
        // UsedAsRateCount. Se excluye además el lote actual para impedir comparaciones
        // entre filas de una misma importación.
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
        var percentage = previousAmount == 0m ? 0m : decimal.Round((delta / previousAmount) * 100m, 2);
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
            logger.LogError(exception, "No se pudieron resolver usuarios de Pricing para la variación de tarifa {ImportRateId}.", currentRate.Id);
            return;
        }

        foreach (var recipient in recipients)
        {
            if (recipient.UserId != Guid.Empty)
            {
                await outbox.WriteAsync(
                    EventName,
                    EventName,
                    BuildRequest("System", (Guid?)recipient.UserId, recipient.Email, recipient.DisplayName, subject, body, payload, currentRate.Id),
                    correlationId: $"pricing-rate-change:{currentRate.Id:N}:system:{recipient.UserId:N}",
                    cancellationToken: cancellationToken
                );
            }

            if (!string.IsNullOrWhiteSpace(recipient.Email))
            {
                await outbox.WriteAsync(
                    EventName,
                    EventName,
                    BuildRequest("Email", (Guid?)recipient.UserId, recipient.Email, recipient.DisplayName, subject, body, payload, currentRate.Id),
                    correlationId: $"pricing-rate-change:{currentRate.Id:N}:email:{recipient.Email}",
                    cancellationToken: cancellationToken
                );
            }
        }
    }

    private static object BuildRequest(
        string channel,
        Guid? userId,
        string? email,
        string? displayName,
        string subject,
        string body,
        object payload,
        Guid importRateId
    ) => new
    {
        notificationType = "pricing.imported-rate.variation",
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
