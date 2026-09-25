using System.Net;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Services;

public sealed class LowMarginApprovedNotificationService(
    IIntegrationEventOutboxWriter outbox,
    ServiceDbContext db,
    AuthSellerDirectoryService authDirectory,
    ILogger<LowMarginApprovedNotificationService> logger)
{
    private const string NotificationEventName = "notifications.notification.requested";
    private const string NotificationType = "pricing.rate.low-margin-approved";

    public async Task QueueAsync(
        Guid rateId,
        Guid? approvedBy,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rate = await db.RateHeaders
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == rateId && !x.IsDeleted,
                    cancellationToken
                );

            if (rate is null)
            {
                logger.LogWarning(
                    "No se pudo notificar la aprobación de margen bajo porque la tarifa {RateId} no existe.",
                    rateId
                );
                return;
            }

            var request = await db.RateRequests
                .AsNoTracking()
                .Where(x => x.RateId == rateId)
                .OrderByDescending(x => x.RequestedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            var recipient = await ResolveRecipientAsync(
                request?.SellerUserId,
                request?.SellerEmail,
                request?.SellerName,
                rate.CreatedBy,
                cancellationToken
            );

            if (recipient is null)
            {
                logger.LogWarning(
                    "La tarifa {RateId} fue aprobada con margen bajo, pero no fue posible resolver al usuario que debe recibir la notificación.",
                    rateId
                );
                return;
            }

            var quoteNumber = rate.QuoNumber ?? rate.RateCode;
            var clientName = string.IsNullOrWhiteSpace(rate.ClientName)
                ? "cliente no especificado"
                : rate.ClientName.Trim();

            var systemBody =
                $"La cotización {quoteNumber} para {clientName} fue aprobada con margen bajo ({rate.MarginPercentage:0.##}%). Ya puede continuar con el proceso de cotización.";

            var emailBody = $"""
                <p>La cotización <strong>{Html(quoteNumber)}</strong> para <strong>{Html(clientName)}</strong> fue aprobada con margen bajo.</p>
                <table style="border-collapse:collapse; width:100%; max-width:680px">
                  <tr><td style="padding:5px 10px"><strong>Cotización</strong></td><td style="padding:5px 10px">{Html(quoteNumber)}</td></tr>
                  <tr><td style="padding:5px 10px"><strong>Cliente</strong></td><td style="padding:5px 10px">{Html(clientName)}</td></tr>
                  <tr><td style="padding:5px 10px"><strong>Margen aprobado</strong></td><td style="padding:5px 10px">{rate.MarginPercentage:0.##}%</td></tr>
                </table>
                <p>La aprobación ya quedó registrada en Pricing y puede continuar con el flujo normal de la cotización.</p>
                """;

            var payload = new
            {
                type = NotificationType,
                rateId = rate.Id,
                rateCode = rate.RateCode,
                quoNumber = quoteNumber,
                clientName = rate.ClientName,
                marginPercentage = rate.MarginPercentage,
                approvedBy,
                action = "open-rate",
                route = $"/pricing/rates/{rate.Id}/wizard",
            };

            var queued = false;

            if (recipient.UserId.HasValue && recipient.UserId.Value != Guid.Empty)
            {
                await QueueAsync(
                    channel: "System",
                    recipient,
                    subject: "Cotización aprobada con margen bajo",
                    body: systemBody,
                    payload,
                    rate.Id,
                    correlationId: $"pricing-low-margin-approved:{rate.Id:N}:rev:{rate.RevisionNumber}:system:{recipient.UserId.Value:N}",
                    cancellationToken
                );
                queued = true;
            }

            if (!string.IsNullOrWhiteSpace(recipient.Email))
            {
                await QueueAsync(
                    channel: "Email",
                    recipient,
                    subject: $"Cotización aprobada - {quoteNumber}",
                    body: emailBody,
                    payload,
                    rate.Id,
                    correlationId: $"pricing-low-margin-approved:{rate.Id:N}:rev:{rate.RevisionNumber}:email:{recipient.Email.Trim().ToLowerInvariant()}",
                    cancellationToken
                );
                queued = true;
            }

            if (!queued)
            {
                logger.LogWarning(
                    "La tarifa {RateId} fue aprobada con margen bajo, pero el destinatario no tiene UserId ni correo.",
                    rate.Id
                );
                return;
            }

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Se encolaron notificaciones System/Email por aprobación de margen bajo para la tarifa {RateId}.",
                rate.Id
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // La notificación no debe revertir una aprobación ya confirmada.
            logger.LogError(
                exception,
                "No se pudo encolar la notificación de aprobación de margen bajo para la tarifa {RateId}.",
                rateId
            );
        }
    }

    private async Task<NotificationRecipient?> ResolveRecipientAsync(
        Guid? sellerUserId,
        string? sellerEmail,
        string? sellerName,
        string? createdBy,
        CancellationToken cancellationToken)
    {
        if (sellerUserId.HasValue && sellerUserId.Value != Guid.Empty)
        {
            var resolvedEmail = Normalize(sellerEmail);
            var resolvedName = Normalize(sellerName);

            if (resolvedEmail is null || resolvedName is null)
            {
                try
                {
                    var seller = await authDirectory.GetSellerAsync(
                        sellerUserId.Value,
                        cancellationToken
                    );
                    resolvedEmail ??= Normalize(seller?.Email);
                    resolvedName ??= DisplayName(seller);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(
                        exception,
                        "No se pudieron completar los datos del vendedor {UserId} desde Auth.",
                        sellerUserId.Value
                    );
                }
            }

            return new NotificationRecipient(
                sellerUserId.Value,
                resolvedEmail,
                resolvedName
            );
        }

        if (!string.IsNullOrWhiteSpace(sellerEmail))
        {
            return new NotificationRecipient(
                null,
                sellerEmail.Trim(),
                Normalize(sellerName)
            );
        }

        if (!Guid.TryParse(createdBy, out var creatorUserId) || creatorUserId == Guid.Empty)
            return null;

        SellerDirectoryUser? creator = null;
        try
        {
            var users = await authDirectory.GetPricingUsersAsync(cancellationToken);
            creator = users.FirstOrDefault(x => x.UserId == creatorUserId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "No se pudieron resolver los datos del creador {UserId} desde Auth.",
                creatorUserId
            );
        }

        return new NotificationRecipient(
            creatorUserId,
            Normalize(creator?.Email),
            DisplayName(creator)
        );
    }

    private Task QueueAsync(
        string channel,
        NotificationRecipient recipient,
        string subject,
        string body,
        object payload,
        Guid rateId,
        string correlationId,
        CancellationToken cancellationToken)
        => outbox.WriteAsync(
            NotificationEventName,
            NotificationEventName,
            new
            {
                notificationType = NotificationType,
                templateCode = (string?)null,
                channel,
                entityType = "RateHeader",
                entityId = rateId.ToString(),
                subject,
                body,
                payload,
                maxAttempts = 5,
                recipients = new[]
                {
                    new
                    {
                        userId = recipient.UserId?.ToString(),
                        address = channel == "Email"
                            ? recipient.Email ?? string.Empty
                            : recipient.UserId?.ToString() ?? string.Empty,
                        displayName = recipient.DisplayName,
                    },
                },
            },
            correlationId: correlationId,
            cancellationToken: cancellationToken
        );

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? DisplayName(SellerDirectoryUser? user)
    {
        if (user is null) return null;
        if (!string.IsNullOrWhiteSpace(user.DisplayName)) return user.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(user.UserName)) return user.UserName.Trim();
        return Normalize(user.Email);
    }

    private static string Html(string? value)
        => WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? "—" : value);

    private sealed record NotificationRecipient(
        Guid? UserId,
        string? Email,
        string? DisplayName
    );
}
