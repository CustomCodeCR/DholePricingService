using System.Net;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Services;

/// <summary>
/// Queues email reminders (5d, 3d, 1d, 6h) and one-day in-app reminders.
/// Each channel has its own PostgreSQL marker committed atomically with the
/// Notifications Outbox event. No direct email or SignalR sends.
/// </summary>
public sealed class SellerRateExpiryReminderWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<SellerRateExpiryReminderWorker> logger) : BackgroundService
{
    private const string EventName = "notifications.notification.requested";
    private const string NotificationType = "pricing.rate.expiry-reminder";
    private static readonly string[] CarbonCopy =
    [
        "cquiros@castrofallas.com",
        "jmesen@castrofallas.com",
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Pricing:ExpiryReminders:Enabled", true))
        {
            logger.LogInformation("Seller expiry reminders are disabled by configuration.");
            return;
        }

        var intervalMinutes = Math.Clamp(
            configuration.GetValue("Pricing:ExpiryReminders:PollingMinutes", 5), 1, 60);

        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "Could not queue seller rate expiry reminders; next poll will retry.");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();
        var sentNotification = scope.ServiceProvider
            .GetRequiredService<SentRateEmailNotificationService>();
        var authDirectory = scope.ServiceProvider
            .GetRequiredService<AuthSellerDirectoryService>();
        var outbox = scope.ServiceProvider
            .GetRequiredService<IIntegrationEventOutboxWriter>();

        var now = DateTimeOffset.UtcNow;
        // A wide UTC bound keeps the database query index-friendly; the precise
        // date-only, Costa Rica business schedule is evaluated in the policy.
        var minUtc = now.UtcDateTime.Date.AddDays(-1);
        var maxUtc = now.UtcDateTime.Date.AddDays(8);
        var candidateIds = await db.RateHeaders.AsNoTracking()
            .Where(rate => !rate.IsDeleted
                && (rate.Status == RateStatus.Sent
                    || rate.Status == RateStatus.RequestedByClient)
                && rate.ValidTo >= minUtc && rate.ValidTo < maxUtc)
            .OrderBy(rate => rate.ValidTo)
            .Select(rate => rate.Id)
            .ToListAsync(cancellationToken);

        foreach (var rateId in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var rate = await db.RateHeaders.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, cancellationToken);

                if (rate is null || rate.Status is not (RateStatus.Sent or RateStatus.RequestedByClient))
                    continue;

                var milestone = SellerRateExpiryReminderPolicy.Due(rate.ValidTo, DateTimeOffset.UtcNow);
                if (milestone is null || SellerRateExpiryReminderPolicy.IsExcludedSeller(rate.ExecutiveName))
                    continue;

                var request = await db.RateRequests.AsNoTracking()
                    .Where(x => x.RateId == rateId)
                    .OrderByDescending(x => x.RequestedAtUtc)
                    .Select(x => new { x.SellerName, x.SellerEmail, x.SellerUserId })
                    .FirstOrDefaultAsync(cancellationToken);

                // A previously queued 1-day email must never suppress the
                // one-day in-app message, or vice versa.
                if (milestone.Key == "1d")
                {
                    try
                    {
                        await QueueOneDaySystemReminderAsync(
                            db, outbox, authDirectory, rate.Id, rate.RevisionNumber,
                            rate.ValidTo, rate.ExecutiveName!,
                            request?.SellerUserId, request?.SellerName, request?.SellerEmail,
                            cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        logger.LogError(exception,
                            "Could not queue one-day in-app reminder for rate {RateId}; email will still be attempted.",
                            rateId);
                    }
                }

                var emails = await sentNotification.ResolveRecipientsAsync(
                    rate.ExecutiveName!,
                    request?.SellerName,
                    request?.SellerEmail,
                    cancellationToken);

                if (emails.Count == 0)
                {
                    logger.LogWarning(
                        "Expiry reminder {Milestone} skipped: no unambiguous seller email for rate {RateId}.",
                        milestone.Key, rateId);
                    continue;
                }

                // Only one recipient is placed in To, as with the original
                // sent-to-seller notification. CC is sent on that same email.
                var to = emails[0];
                var cc = CarbonCopy
                    .Where(address => !string.Equals(address, to, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

                // Revalidate after the directory lookup; acceptance, rejection,
                // cancellation, expiry or a newly prepared revision stops reminders.
                var current = await db.RateHeaders.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, cancellationToken);
                if (current is null
                    || current.Status is not (RateStatus.Sent or RateStatus.RequestedByClient)
                    || current.RevisionNumber != rate.RevisionNumber
                    || current.ValidTo.Date != rate.ValidTo.Date
                    || !string.Equals(current.ExecutiveName?.Trim(), rate.ExecutiveName?.Trim(),
                        StringComparison.OrdinalIgnoreCase)
                    || SellerRateExpiryReminderPolicy.Due(current.ValidTo, DateTimeOffset.UtcNow)?.Key
                        != milestone.Key)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    continue;
                }

                // ON CONFLICT DO NOTHING serializes competing replicas on the
                // composite primary key. A failed enqueue rolls the marker back.
                var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO pricing."RateExpiryReminderQueue"
                        ("RateId", "RevisionNumber", "ValidToDate", "Milestone")
                    VALUES ({current.Id}, {current.RevisionNumber},
                            {DateOnly.FromDateTime(current.ValidTo)}, {milestone.Key})
                    ON CONFLICT DO NOTHING
                    """, cancellationToken);

                if (claimed == 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    continue;
                }

                var quo = FirstNonEmpty(current.QuoNumber, current.RateCode)
                    ?? current.Id.ToString("D");
                var customer = FirstNonEmpty(current.ClientName, request?.SellerName) ?? "Cliente";
                var route = string.Join(" → ", new[]
                {
                    current.PolName, current.PoeName, current.PodName,
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
                var relativeRoute = $"/pricing/seller-rates/{current.Id:D}";
                var configuredUrl = configuration["Pricing:PublicWebBaseUrl"];
                var baseUrl = Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri)
                              && uri.Scheme is "http" or "https"
                    ? uri.GetLeftPart(UriPartial.Authority)
                    : "https://sistema.logisticacastrofallas.com";
                var url = baseUrl + relativeRoute;
                var dateText = current.ValidTo.ToString("dd/MM/yyyy");
                var body = $"""
                    <p>La tarifa <strong>{Html(quo)}</strong> está próxima a vencer.
                    Quedan aproximadamente <strong>{Html(milestone.Label)}</strong> para el cierre de su vigencia.</p>
                    <table style="border-collapse:collapse;width:100%;max-width:740px">
                      <tr><td style="padding:6px 10px"><strong>Cliente</strong></td><td>{Html(customer)}</td></tr>
                      <tr><td style="padding:6px 10px"><strong>Vendedor</strong></td><td>{Html(current.ExecutiveName)}</td></tr>
                      <tr><td style="padding:6px 10px"><strong>Ruta</strong></td><td>{Html(route)}</td></tr>
                      <tr><td style="padding:6px 10px"><strong>Válida hasta</strong></td><td>{Html(dateText)}, 11:59 p. m. (Costa Rica)</td></tr>
                    </table>
                    <p>Revise la tarifa y dé seguimiento al cliente antes de su vencimiento.</p>
                    <p><a href="{Html(url)}" style="display:inline-block;padding:12px 18px;background:#eb3a22;color:#fff;text-decoration:none;border-radius:8px;font-weight:700">Ver tarifa {Html(quo)}</a></p>
                    <p>Enlace directo: <a href="{Html(url)}">{Html(url)}</a></p>
                    """;

                await outbox.WriteAsync(
                    EventName, EventName,
                    new
                    {
                        notificationType = NotificationType,
                        channel = "Email",
                        entityType = "RateHeader",
                        entityId = current.Id.ToString("D"),
                        subject = $"Recordatorio de vencimiento - {quo} - {milestone.Label}",
                        body,
                        payload = new
                        {
                            rateId = current.Id,
                            quoNumber = quo,
                            clientName = customer,
                            sellerName = current.ExecutiveName,
                            expiryDate = dateText,
                            milestone = milestone.Key,
                            action = "open-rate",
                            route = relativeRoute,
                            url,
                            cc,
                        },
                        maxAttempts = 5,
                        recipients = new[]
                        {
                            new
                            {
                                userId = (string?)null,
                                address = to,
                                displayName = current.ExecutiveName,
                            },
                        },
                    },
                    correlationId: $"pricing-rate-expiry:{current.Id:N}:rev:{current.RevisionNumber}:to:{current.ValidTo:yyyyMMdd}:{milestone.Key}",
                    cancellationToken: cancellationToken);

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                logger.LogInformation(
                    "Queued {Milestone} expiry reminder for rate {RateId}, revision {Revision}.",
                    milestone.Key, current.Id, current.RevisionNumber);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A failure for one quotation must not prevent notifying other
                // sellers. The transaction rolls back on disposal.
                logger.LogError(exception,
                    "Could not queue expiry reminder for rate {RateId}.", rateId);
            }
        }
    }


    private async Task QueueOneDaySystemReminderAsync(
        ServiceDbContext db,
        IIntegrationEventOutboxWriter outbox,
        AuthSellerDirectoryService authDirectory,
        Guid rateId,
        int revisionNumber,
        DateTime validTo,
        string executiveName,
        Guid? requestSellerUserId,
        string? requestSellerName,
        string? requestSellerEmail,
        CancellationToken cancellationToken)
    {
        var userId = SellerRateExpiryReminderPolicy.ResolveSellerUserId(
            executiveName, requestSellerUserId, requestSellerName, requestSellerEmail, []);

        if (userId is null)
        {
            // The executive assigned to the rate takes precedence over the
            // original request seller. Never notify another seller by guess.
            var executives = await authDirectory.GetSalesExecutivesAsync(cancellationToken);
            var sellers = await authDirectory.GetSellersAsync(cancellationToken);
            userId = SellerRateExpiryReminderPolicy.ResolveSellerUserId(
                executiveName, requestSellerUserId, requestSellerName, requestSellerEmail,
                executives.Concat(sellers));
        }

        if (userId is null)
        {
            logger.LogWarning(
                "One-day System reminder skipped: no unique Auth user for executive {ExecutiveName} on rate {RateId}.",
                executiveName, rateId);
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var current = await db.RateHeaders.AsNoTracking()
            .SingleOrDefaultAsync(rate => rate.Id == rateId && !rate.IsDeleted, cancellationToken);

        if (current is null
            || current.Status is not (RateStatus.Sent or RateStatus.RequestedByClient)
            || current.RevisionNumber != revisionNumber
            || current.ValidTo.Date != validTo.Date
            || !string.Equals(current.ExecutiveName?.Trim(), executiveName.Trim(),
                StringComparison.OrdinalIgnoreCase)
            || SellerRateExpiryReminderPolicy.Due(current.ValidTo, DateTimeOffset.UtcNow)?.Key != "1d")
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        // Independent marker: System still sends when an earlier release
        // already inserted the original "1d" email marker.
        const string systemMilestone = "1d-system";
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO pricing."RateExpiryReminderQueue"
                ("RateId", "RevisionNumber", "ValidToDate", "Milestone")
            VALUES ({current.Id}, {current.RevisionNumber},
                    {DateOnly.FromDateTime(current.ValidTo)}, {systemMilestone})
            ON CONFLICT DO NOTHING
            """, cancellationToken);

        if (claimed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        var quo = FirstNonEmpty(current.QuoNumber, current.RateCode) ?? current.Id.ToString("D");
        var clientName = FirstNonEmpty(current.ClientName) ?? "Cliente";
        var expirationDate = current.ValidTo.ToString("dd/MM/yyyy");
        var route = $"/pricing/seller-rates/{current.Id:D}";
        var correlationId = $"pricing-expiry:{current.Id:N}:r{current.RevisionNumber}:{current.ValidTo:yyyyMMdd}:system-1d";

        await outbox.WriteAsync(
            EventName, EventName,
            new
            {
                notificationType = NotificationType,
                idempotencyKey = correlationId,
                channel = "System",
                entityType = "RateHeader",
                entityId = current.Id.ToString("D"),
                subject = $"La tarifa {quo} vence mañana",
                body = $"La tarifa {quo} del cliente {clientName} vence mañana, {expirationDate}, " +
                       "a las 11:59 p. m. (Costa Rica). Revísela y dé seguimiento al cliente.",
                payload = new
                {
                    rateId = current.Id,
                    quoNumber = quo,
                    clientName,
                    sellerName = current.ExecutiveName,
                    expiryDate = expirationDate,
                    milestone = "1d",
                    action = "open-rate",
                    route,
                },
                maxAttempts = 5,
                recipients = new[]
                {
                    new
                    {
                        userId = userId.Value.ToString("D"),
                        address = userId.Value.ToString("D"),
                        displayName = current.ExecutiveName,
                    },
                },
            },
            correlationId: correlationId,
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(
            "Queued one-day System reminder for rate {RateId}, revision {Revision}, user {UserId}.",
            current.Id, current.RevisionNumber, userId.Value);
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string Html(string? value)
        => WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? "—" : value);
}
