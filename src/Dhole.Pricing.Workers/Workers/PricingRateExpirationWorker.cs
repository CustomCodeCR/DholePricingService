using CustomCodeFramework.Workers.Abstractions;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Worker.Workers;

internal sealed class PricingRateExpirationWorker(
    ServiceDbContext dbContext,
    IIntegrationEventOutboxWriter outbox,
    IPricingNotificationRecipientProvider recipientProvider,
    IConfiguration configuration,
    ILogger<PricingRateExpirationWorker> logger
) : IBackgroundWorker
{
    private const string EventName = "notifications.notification.requested";
    private const string NotificationType = "pricing.rate.expired";

    public string Name => "pricing.rate-expiration";

    public async Task ExecuteAsync(
        IWorkerExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        if (!configuration.GetValue("Pricing:RateExpiration:Enabled", true))
            return;

        var todayUtc = DateTime.UtcNow.Date;
        var batchSize = Math.Clamp(
            configuration.GetValue("Pricing:RateExpiration:BatchSize", 250),
            1,
            1000
        );

        var rates = await dbContext
            .RateHeaders.Where(rate =>
                !rate.IsDeleted
                && rate.ValidTo < todayUtc
                && rate.Status == RateStatus.Sent
            )
            .OrderBy(rate => rate.ValidTo)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (rates.Count == 0)
        {
            logger.LogDebug("No hay tarifas enviadas pendientes de marcar como vencidas.");
            return;
        }

        var rateIds = rates.Select(rate => rate.Id).ToArray();
        var sellerRequests = await dbContext.RateRequests
            .AsNoTracking()
            .Where(request => request.RateId.HasValue && rateIds.Contains(request.RateId.Value))
            .ToListAsync(cancellationToken);

        IReadOnlyCollection<PricingNotificationRecipient> pricingRecipients = Array.Empty<PricingNotificationRecipient>();
        try
        {
            pricingRecipients = await recipientProvider.GetPricingRecipientsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "No se pudieron resolver operativos de Pricing para las alertas de tarifas vencidas.");
        }

        var expiredCount = 0;

        foreach (var rate in rates)
        {
            if (!rate.MarkExpired(todayUtc))
                continue;

            expiredCount++;

            var linkedRequest = sellerRequests
                .Where(request => request.RateId == rate.Id)
                .OrderByDescending(request => request.RequestedAtUtc)
                .FirstOrDefault();

            var recipients = new Dictionary<Guid, ExpirationRecipient>();
            foreach (var recipient in pricingRecipients.Where(recipient => recipient.UserId != Guid.Empty))
            {
                recipients[recipient.UserId] = new ExpirationRecipient(
                    recipient.UserId,
                    recipient.Email,
                    recipient.DisplayName ?? recipient.UserName
                );
            }

            if (linkedRequest?.SellerUserId is Guid sellerUserId && sellerUserId != Guid.Empty)
            {
                recipients[sellerUserId] = new ExpirationRecipient(
                    sellerUserId,
                    linkedRequest.SellerEmail,
                    linkedRequest.SellerName ?? linkedRequest.ExecutiveName
                );
            }

            var code = rate.QuoNumber ?? rate.RateCode;
            var route = $"{rate.PolName} → {rate.PoeName}{(string.IsNullOrWhiteSpace(rate.PodName) ? string.Empty : $" → {rate.PodName}")}";
            var subject = $"Tarifa vencida · {code}";
            var body =
                $"La tarifa {code} para {rate.ClientName ?? "cliente sin definir"} venció el {rate.ValidTo:dd/MM/yyyy}. "
                + $"Ruta: {route}. Estado actualizado a Vencida. Revise la tarifa para renovarla o crear una nueva alternativa.";
            var payload = new
            {
                type = NotificationType,
                rateId = rate.Id,
                rate.RateCode,
                rate.QuoNumber,
                rate.ClientName,
                rate.ExecutiveName,
                rate.ValidTo,
                route,
                action = "open-rate",
                url = $"/pricing/rates?rateId={rate.Id}",
            };

            foreach (var recipient in recipients.Values)
            {
                await QueueSystemAsync(rate.Id, recipient, subject, body, payload, cancellationToken);
                if (!string.IsNullOrWhiteSpace(recipient.Email))
                    await QueueEmailAsync(rate.Id, recipient, subject, body, payload, cancellationToken);
            }
        }

        if (expiredCount == 0)
            return;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Se marcaron {ExpiredCount} tarifas como vencidas y se encolaron alertas por correo/SignalR para la fecha UTC {ExpirationDate}.",
            expiredCount,
            todayUtc
        );
    }

    private Task QueueSystemAsync(
        Guid rateId,
        ExpirationRecipient recipient,
        string subject,
        string body,
        object payload,
        CancellationToken cancellationToken
    ) => outbox.WriteAsync(
        EventName,
        EventName,
        new
        {
            notificationType = NotificationType,
            templateCode = (string?)null,
            channel = "System",
            entityType = "Rate",
            entityId = rateId.ToString(),
            subject,
            body,
            payload,
            maxAttempts = 3,
            recipients = new[]
            {
                new
                {
                    userId = recipient.UserId.ToString(),
                    address = recipient.UserId.ToString(),
                    displayName = recipient.DisplayName,
                },
            },
        },
        correlationId: $"pricing-rate-expired:{rateId:N}:system:{recipient.UserId:N}",
        cancellationToken: cancellationToken
    );

    private Task QueueEmailAsync(
        Guid rateId,
        ExpirationRecipient recipient,
        string subject,
        string body,
        object payload,
        CancellationToken cancellationToken
    ) => outbox.WriteAsync(
        EventName,
        EventName,
        new
        {
            notificationType = NotificationType,
            templateCode = (string?)null,
            channel = "Email",
            entityType = "Rate",
            entityId = rateId.ToString(),
            subject,
            body,
            payload,
            maxAttempts = 3,
            recipients = new[]
            {
                new
                {
                    userId = recipient.UserId.ToString(),
                    address = recipient.Email ?? string.Empty,
                    displayName = recipient.DisplayName,
                },
            },
        },
        correlationId: $"pricing-rate-expired:{rateId:N}:email:{recipient.UserId:N}",
        cancellationToken: cancellationToken
    );

    private sealed record ExpirationRecipient(Guid UserId, string? Email, string? DisplayName);
}
