using CustomCodeFramework.Workers.Abstractions;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Worker.Workers;

internal sealed class PricingRateRequestSlaWorker(
    ServiceDbContext dbContext,
    IIntegrationEventOutboxWriter outbox,
    IPricingNotificationRecipientProvider recipientProvider,
    IConfiguration configuration,
    ILogger<PricingRateRequestSlaWorker> logger
) : IBackgroundWorker
{
    private const string EventName = "notifications.notification.requested";

    public string Name => "pricing.rate-request-sla";

    public async Task ExecuteAsync(
        IWorkerExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        if (!configuration.GetValue("Pricing:RateRequestSla:Enabled", true))
            return;

        var now = DateTime.UtcNow;
        await CompleteSentRequestsAsync(now, cancellationToken);

        var batchSize = Math.Clamp(
            configuration.GetValue("Pricing:RateRequestSla:BatchSize", 100),
            1,
            500
        );

        var overdue = await dbContext.RateRequests
            .Where(x => x.Status == RateRequestStatus.Open
                && x.DueAtUtc <= now
                && x.SlaReminderSentAtUtc == null)
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.DueAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (overdue.Count == 0) return;

        IReadOnlyCollection<PricingNotificationRecipient> recipients;
        try
        {
            recipients = await recipientProvider.GetPricingRecipientsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "No se pudieron resolver operativos de Pricing para alertas SLA.");
            return;
        }

        var emailRecipients = recipients
            .Where(x => !string.IsNullOrWhiteSpace(x.Email))
            .GroupBy(x => x.Email!, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();

        if (emailRecipients.Length == 0)
        {
            logger.LogWarning("No existen operativos de Pricing con correo para alertas SLA.");
            return;
        }

        foreach (var request in overdue)
        {
            // Puede haberse enviado entre la consulta inicial y este punto.
            if (request.RateId.HasValue && await IsRateAlreadySentAsync(request.RateId.Value, cancellationToken))
            {
                request.MarkCompleted(now);
                continue;
            }

            var priorityLabel = request.Priority switch
            {
                RateRequestPriority.Green => "Verde · máximo 24 horas",
                RateRequestPriority.Yellow => "Amarillo · máximo 48 horas",
                RateRequestPriority.Red => "Rojo · máximo 72 horas",
                _ => request.Priority.ToString(),
            };
            var route = $"{request.OriginName ?? "Origen pendiente"} → {request.DestinationName ?? "Destino pendiente"}";
            var subject = $"Solicitud de tarifa {request.Priority} vencida";
            var body =
                $"La solicitud de tarifa abierta excedió su tiempo máximo de atención. "
                + $"Prioridad: {priorityLabel}. Cliente: {request.ClientName ?? "Sin definir"}. "
                + $"Ruta: {route}. Solicitada: {request.RequestedAtUtc:yyyy-MM-dd HH:mm} UTC. "
                + $"Límite: {request.DueAtUtc:yyyy-MM-dd HH:mm} UTC. Abra Pricing > Tarifas para completarla y enviarla.";

            var payload = new
            {
                type = "pricing.rate-request.sla-overdue",
                rateRequestId = request.Id,
                priority = request.Priority.ToString(),
                request.RequestedAtUtc,
                request.DueAtUtc,
                request.ClientName,
                request.OriginName,
                request.DestinationName,
                action = "complete-rate-request",
                route = "/pricing/rates",
            };

            foreach (var recipient in emailRecipients)
            {
                await outbox.WriteAsync(
                    EventName,
                    EventName,
                    new
                    {
                        notificationType = "pricing.rate-request.sla-overdue",
                        templateCode = (string?)null,
                        channel = "Email",
                        entityType = "RateRequest",
                        entityId = request.Id.ToString(),
                        subject,
                        body,
                        payload,
                        maxAttempts = 3,
                        recipients = new[]
                        {
                            new
                            {
                                userId = recipient.UserId == Guid.Empty ? null : recipient.UserId.ToString(),
                                address = recipient.Email ?? string.Empty,
                                displayName = recipient.DisplayName,
                            },
                        },
                    },
                    correlationId: $"pricing-rate-request-sla:{request.Id:N}:{recipient.Email}",
                    cancellationToken: cancellationToken
                );
            }

            request.MarkSlaReminderSent(now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task CompleteSentRequestsAsync(DateTime now, CancellationToken cancellationToken)
    {
        var linked = await dbContext.RateRequests
            .Where(x => x.Status == RateRequestStatus.Open && x.RateId.HasValue)
            .ToListAsync(cancellationToken);
        if (linked.Count == 0) return;

        var ids = linked.Select(x => x.RateId!.Value).Distinct().ToArray();
        var sentIds = (await dbContext.RateHeaders
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id)
                && (x.Status == RateStatus.Sent
                    || x.Status == RateStatus.AcceptedByClient
                    || x.Status == RateStatus.RejectedByClient
                    || x.Status == RateStatus.Closed))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        if (sentIds.Count == 0) return;
        foreach (var request in linked.Where(x => sentIds.Contains(x.RateId!.Value)))
            request.MarkCompleted(now);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task<bool> IsRateAlreadySentAsync(Guid rateId, CancellationToken cancellationToken)
        => dbContext.RateHeaders.AsNoTracking().AnyAsync(
            x => x.Id == rateId
                && (x.Status == RateStatus.Sent
                    || x.Status == RateStatus.AcceptedByClient
                    || x.Status == RateStatus.RejectedByClient
                    || x.Status == RateStatus.Closed),
            cancellationToken
        );
}
