using System.Security.Claims;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;

namespace Dhole.Pricing.Api.Middleware;

/// <summary>
/// Keeps creation authorization separate from approval authorization.
/// A user with pricing.rate.create may always submit the creation wizard. If the resulting
/// margin is below the configured minimum and the user cannot approve low margins, the domain
/// leaves the rate in PendingApproval and this middleware notifies the actual approvers.
/// </summary>
public sealed class RateCreationApprovalMiddleware(RequestDelegate next)
{
    private const string RatesPath = "/api/pricing/rates";
    private const string NotificationEventName = "notifications.notification.requested";
    private const string LowMarginNotificationType = "pricing.rate.low-margin-approval-required";

    public async Task InvokeAsync(
        HttpContext context,
        ServiceDbContext db,
        SellerVisibilityService visibilityService,
        IPricingNotificationRecipientProvider recipientProvider,
        IIntegrationEventOutboxWriter outbox,
        ILogger<RateCreationApprovalMiddleware> logger)
    {
        if (!IsRateCreateRequest(context.Request)
            || context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var canCreateRate = visibilityService.HasScope(
            context.User,
            PricingConstants.Scopes.RateCreate
        );

        if (!canCreateRate)
        {
            await next(context);
            return;
        }

        var canApproveLowMargin = visibilityService.HasScope(
            context.User,
            PricingConstants.Scopes.RateApproveLowMargin
        );

        // The official create endpoint already requires pricing.rate.create. Older revisions
        // additionally required pricing.import-fcl-rate.create-as-rate whenever the wizard kept
        // the selected imported source id. That secondary check incorrectly produced a 403 for
        // normal Pricing users. Grant compatibility only for this POST and only for a principal
        // that already has the canonical create permission.
        AddRequestOnlyScopeIfMissing(
            context.User,
            visibilityService,
            PricingConstants.Scopes.ImportFclRateCreateAsRate
        );

        var previouslyTrackedRateIds = db.ChangeTracker
            .Entries<RateHeader>()
            .Select(entry => entry.Entity.Id)
            .ToHashSet();

        await next(context);

        if (context.Response.StatusCode < StatusCodes.Status200OK
            || context.Response.StatusCode >= StatusCodes.Status300MultipleChoices
            || canApproveLowMargin)
        {
            return;
        }

        var pendingRate = db.ChangeTracker
            .Entries<RateHeader>()
            .Select(entry => entry.Entity)
            .FirstOrDefault(rate =>
                !previouslyTrackedRateIds.Contains(rate.Id)
                && !rate.IsDeleted
                && rate.RequiredApproval
                && rate.Status == RateStatus.PendingApproval
            );

        if (pendingRate is null)
            return;

        try
        {
            await QueueLowMarginApprovalNotificationsAsync(
                pendingRate,
                recipientProvider,
                outbox,
                logger,
                context.RequestAborted
            );

            await db.SaveChangesAsync(context.RequestAborted);
        }
        catch (Exception exception)
        {
            // Notifications must never turn a successfully-created rate into an HTTP failure.
            logger.LogError(
                exception,
                "La tarifa {RateId} se creó pendiente de aprobación, pero no fue posible encolar la notificación de margen bajo.",
                pendingRate.Id
            );
        }
    }

    private static bool IsRateCreateRequest(HttpRequest request)
        => HttpMethods.IsPost(request.Method)
            && string.Equals(
                request.Path.Value?.TrimEnd('/'),
                RatesPath,
                StringComparison.OrdinalIgnoreCase
            );

    private static void AddRequestOnlyScopeIfMissing(
        ClaimsPrincipal user,
        SellerVisibilityService visibilityService,
        string scope)
    {
        if (visibilityService.HasScope(user, scope))
            return;

        var identity = user.Identities.FirstOrDefault(candidate => candidate.IsAuthenticated);
        identity?.AddClaim(new Claim("scope", scope));
    }

    private static async Task QueueLowMarginApprovalNotificationsAsync(
        RateHeader rate,
        IPricingNotificationRecipientProvider recipientProvider,
        IIntegrationEventOutboxWriter outbox,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<PricingNotificationRecipient> recipients;
        try
        {
            recipients = await recipientProvider.GetRecipientsByScopeAsync(
                PricingConstants.Scopes.RateApproveLowMargin,
                cancellationToken
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "No se pudieron resolver usuarios con el scope {Scope} para aprobar la tarifa {RateId}.",
                PricingConstants.Scopes.RateApproveLowMargin,
                rate.Id
            );
            return;
        }

        if (recipients.Count == 0)
        {
            logger.LogWarning(
                "La tarifa {RateId} quedó pendiente por margen bajo, pero no existen usuarios activos con el scope {Scope}.",
                rate.Id,
                PricingConstants.Scopes.RateApproveLowMargin
            );
            return;
        }

        var subject = "Tarifa pendiente de aprobación por margen bajo";
        var clientLabel = string.IsNullOrWhiteSpace(rate.ClientName)
            ? "cliente no especificado"
            : rate.ClientName.Trim();
        var body =
            $"La tarifa {rate.RateCode} para {clientLabel} quedó pendiente de aprobación porque su margen es {rate.MarginPercentage:0.##}%. "
            + "Revise la tarifa en Pricing y apruebe o rechace el margen.";

        var payload = new
        {
            type = LowMarginNotificationType,
            rateId = rate.Id,
            rateCode = rate.RateCode,
            clientName = rate.ClientName,
            marginPercentage = rate.MarginPercentage,
            requiredScope = PricingConstants.Scopes.RateApproveLowMargin,
            action = "review-low-margin",
            route = $"/pricing/rates/{rate.Id}/wizard",
        };

        foreach (var recipient in recipients)
        {
            if (recipient.UserId != Guid.Empty)
            {
                await QueueAsync(
                    outbox,
                    "System",
                    recipient,
                    subject,
                    body,
                    payload,
                    rate.Id,
                    $"pricing-low-margin:{rate.Id:N}:system:{recipient.UserId:N}",
                    cancellationToken
                );
            }

            if (!string.IsNullOrWhiteSpace(recipient.Email))
            {
                await QueueAsync(
                    outbox,
                    "Email",
                    recipient,
                    subject,
                    body,
                    payload,
                    rate.Id,
                    $"pricing-low-margin:{rate.Id:N}:email:{recipient.Email}",
                    cancellationToken
                );
            }
        }
    }

    private static Task QueueAsync(
        IIntegrationEventOutboxWriter outbox,
        string channel,
        PricingNotificationRecipient recipient,
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
                notificationType = LowMarginNotificationType,
                templateCode = (string?)null,
                channel,
                entityType = "RateHeader",
                entityId = rateId.ToString(),
                subject,
                body,
                payload,
                maxAttempts = 3,
                recipients = new[]
                {
                    new
                    {
                        userId = recipient.UserId == Guid.Empty
                            ? null
                            : recipient.UserId.ToString(),
                        address = channel == "Email"
                            ? recipient.Email ?? string.Empty
                            : recipient.UserId == Guid.Empty
                                ? string.Empty
                                : recipient.UserId.ToString(),
                        displayName = recipient.DisplayName,
                    },
                },
            },
            correlationId: correlationId,
            cancellationToken: cancellationToken
        );
}
