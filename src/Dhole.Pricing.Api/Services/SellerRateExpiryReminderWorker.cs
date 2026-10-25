using System.Net;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Services;

/// <summary>
/// Checks already-issued quotations and enqueues 5d, 3d, 1d and 6h reminders.
/// A PostgreSQL unique marker and the Notifications Outbox event commit in
/// the same transaction, preventing duplicated emails after restarts or
/// concurrent API replicas. Does not send mail directly.
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
        var outbox = scope.ServiceProvider
            .GetRequiredService<IIntegrationEventOutboxWriter>();

        var now = DateTimeOffset.UtcNow;
        var nowInCostaRica = TimeZoneInfo.ConvertTime(
            now, SellerRateExpiryReminderPolicy.CostaRicaZone);
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
                    .Select(x => new { x.SellerName, x.SellerEmail })
                    .FirstOrDefaultAsync(cancellationToken);

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

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string Html(string? value)
        => WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? "—" : value);
}
