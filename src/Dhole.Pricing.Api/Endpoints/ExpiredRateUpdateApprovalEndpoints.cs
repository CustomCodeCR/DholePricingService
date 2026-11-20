using System.Net;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// Supervisor consent for editing an expired commercial quote. The reason and
/// reviewer identity are stored durably, and delivery goes through Notifications
/// Outbox (System -> SignalR; Email -> SMTP), never direct best-effort sends.
/// </summary>
public static class ExpiredRateUpdateApprovalEndpoints
{
    private const string Event = "notifications.notification.requested";
    private static readonly string[] SupervisorEmails =
        ["rsibaja@castrofallas.com", "jmesen@castrofallas.com"];

    public sealed class ApprovalRow
    {
        public Guid Id { get; set; }
        public Guid RateId { get; set; }
        public int RevisionNumber { get; set; }
        public string Reason { get; set; } = "";
        public Guid RequestedByUserId { get; set; }
        public DateTime RequestedAtUtc { get; set; }
        public string Status { get; set; } = "";
        public Guid? DecidedByUserId { get; set; }
        public DateTime? DecidedAtUtc { get; set; }
        public string? DecisionReason { get; set; }
    }

    public sealed record RequestApproval(string? Reason);
    public sealed record DecideApproval(bool Approve, string? Reason);

    public static IEndpointRouteBuilder MapExpiredRateUpdateApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing").RequireAuthorization()
            .WithTags("Expired rate update approvals");

        group.MapGet("/rates/{rateId:guid}/expired-update-approval", GetAsync);
        group.MapPost("/rates/{rateId:guid}/expired-update-approval", RequestAsync);
        group.MapGet("/expired-rate-updates/pending", PendingAsync);
        group.MapPost("/expired-rate-updates/{approvalId:guid}/decision", DecideAsync);

        return app;
    }

    private static bool HasPricingUpdateScope(HttpContext http)
        => http.User.Claims
            .Where(c => c.Type.Equals("scope", StringComparison.OrdinalIgnoreCase)
                     || c.Type.Equals("scp", StringComparison.OrdinalIgnoreCase))
            .SelectMany(c => c.Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
            .Any(value => value.Equals("pricing.rate.update", StringComparison.OrdinalIgnoreCase));

    private static async Task<bool> CanReadAsync(
        RateHeader rate, HttpContext http, SellerRateAccessService access, CancellationToken ct)
    {
        var userId = http.GetCurrentUserId();
        return userId.HasValue && (HasPricingUpdateScope(http)
            || await access.CanReadAsync(rate, userId.Value, ct));
    }

    private static async Task<List<ApprovalRow>> HistoryAsync(
        ServiceDbContext db, Guid rateId, CancellationToken ct)
        => await db.Database.SqlQueryRaw<ApprovalRow>(
            """
            SELECT "Id", "RateId", "RevisionNumber", "Reason",
                   "RequestedByUserId", "RequestedAtUtc", "Status",
                   "DecidedByUserId", "DecidedAtUtc", "DecisionReason"
              FROM pricing."ExpiredRateUpdateApprovals"
             WHERE "RateId" = {0}
             ORDER BY "RequestedAtUtc" DESC, "Id" DESC
            """, rateId).ToListAsync(ct);

    private static async Task<IReadOnlyList<SellerDirectoryUser>> SupervisorsAsync(
        AuthSellerDirectoryService directory, CancellationToken ct)
    {
        // Auth user IDs, never display names or user-supplied email, determine access.
        var users = await directory.GetPricingUsersAsync(ct);
        return SupervisorEmails.Select(email => users
            .Where(u => string.Equals(u.Email?.Trim(), email, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(u => u.UserId).ToArray())
            .Where(matches => matches.Length == 1)
            .Select(matches => matches[0])
            .ToArray();
    }

    private static async Task<IResult> GetAsync(
        Guid rateId, ServiceDbContext db, SellerRateAccessService access,
        HttpContext http, CancellationToken ct)
    {
        var rate = await db.RateHeaders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, ct);
        if (rate is null) return Results.NotFound();
        if (!await CanReadAsync(rate, http, access, ct)) return Results.Forbid();

        var expired = ExpiredRateUpdateApprovalPolicy.RequiresApproval(rate, DateTimeOffset.UtcNow);
        var history = await HistoryAsync(db, rate.Id, ct);
        var approval = history.FirstOrDefault(x =>
            x.RevisionNumber == rate.RevisionNumber && x.Status == "Approved");
        return Results.Ok(new
        {
            rateId, rate.RevisionNumber,
            requiresApproval = expired,
            canUpdate = !expired || approval is not null,
            currentApproval = history.FirstOrDefault(x => x.RevisionNumber == rate.RevisionNumber),
            history,
        });
    }

    private static async Task<IResult> RequestAsync(
        Guid rateId, RequestApproval request, ServiceDbContext db,
        SellerRateAccessService access, AuthSellerDirectoryService directory,
        IIntegrationEventOutboxWriter outbox, IConfiguration configuration,
        HttpContext http, CancellationToken ct)
    {
        var requesterId = http.GetCurrentUserId();
        if (requesterId is null || requesterId == Guid.Empty) return Results.Unauthorized();
        var reason = request.Reason?.Trim();
        if (reason is null || reason.Length < 15 || reason.Length > 4000)
            return Results.BadRequest(new {
                code = "Pricing.ExpiryReasonRequired",
                message = "Indique por qué dejó vencer la tarifa (15 a 4000 caracteres).",
            });

        var rate = await db.RateHeaders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == rateId && !x.IsDeleted, ct);
        if (rate is null) return Results.NotFound();
        if (!await CanReadAsync(rate, http, access, ct)) return Results.Forbid();
        if (rate.Status.ToString() is "Closed" or "Cancelled")
            return Results.Conflict(new { code = "Pricing.RateClosed",
                message = "Una cotización cerrada o cancelada no se puede reactivar." });
        if (!ExpiredRateUpdateApprovalPolicy.RequiresApproval(rate, DateTimeOffset.UtcNow))
            return Results.Conflict(new { code = "Pricing.RateNotExpired",
                message = "Esta tarifa todavía está vigente y no necesita aprobación por vencimiento." });

        // Both named supervisors must be addressable on System and Email.
        var managers = await SupervisorsAsync(directory, ct);
        if (managers.Count != SupervisorEmails.Length)
            return Results.Problem(
                title: "No se pudieron identificar ambos jefes en Auth para las notificaciones.",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        var approvalId = Guid.NewGuid();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO pricing."ExpiredRateUpdateApprovals"
              ("Id","RateId","RevisionNumber","Reason","RequestedByUserId","Status")
            VALUES ({approvalId}, {rate.Id}, {rate.RevisionNumber},
                    {reason}, {requesterId.Value}, 'Pending')
            ON CONFLICT DO NOTHING
            """, ct);
        if (inserted != 1)
            return Results.Conflict(new {
                code = "Pricing.ExpiredUpdateAlreadyRequested",
                message = "Ya existe una solicitud pendiente o autorizada para esta revisión.",
            });

        var quote = rate.QuoNumber ?? rate.RateCode;
        // /home is accessible to sellers, unlike the Pricing rates management route.
        var route = "/home?expiredApprovals=1";
        var subject = "Autorización requerida para actualizar la tarifa vencida " + quote;
        var body = "Se solicita autorización para actualizar la tarifa " + quote
            + " del cliente " + (rate.ClientName ?? "sin especificar")
            + ". Motivo del vencimiento: " + reason;
        foreach (var manager in managers)
        {
            await QueueAsync(outbox, "System", manager, rate.Id, approvalId,
                "pricing.rate.expired-update.requested", subject, body, route, ct);
            await QueueAsync(outbox, "Email", manager, rate.Id, approvalId,
                "pricing.rate.expired-update.requested", subject,
                "<p>" + WebUtility.HtmlEncode(body) + "</p><p>Revise la solicitud en Dhole.</p>",
                route, ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(new { approvalId, status = "Pending", rateId, rate.RevisionNumber });
    }

    private static async Task<IResult> PendingAsync(
        ServiceDbContext db, AuthSellerDirectoryService directory,
        HttpContext http, CancellationToken ct)
    {
        var reviewer = await ReviewerAsync(directory, http, ct);
        if (reviewer is null) return Results.Forbid();
        var rows = await db.Database.SqlQueryRaw<ApprovalRow>(
            """
            SELECT a."Id", a."RateId", a."RevisionNumber", a."Reason",
                   a."RequestedByUserId", a."RequestedAtUtc", a."Status",
                   a."DecidedByUserId", a."DecidedAtUtc", a."DecisionReason"
              FROM pricing."ExpiredRateUpdateApprovals" a
              JOIN pricing."RateHeaders" r ON r.id = a."RateId"
             WHERE a."Status" = 'Pending' AND NOT r.is_deleted
               AND r.revision_number = a."RevisionNumber"
             ORDER BY a."RequestedAtUtc" DESC
            """).ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<SellerDirectoryUser?> ReviewerAsync(
        AuthSellerDirectoryService directory, HttpContext http, CancellationToken ct)
    {
        var userId = http.GetCurrentUserId();
        if (userId is null) return null;
        var supervisors = await SupervisorsAsync(directory, ct);
        return supervisors.SingleOrDefault(u => u.UserId == userId.Value);
    }

    private static async Task<IResult> DecideAsync(
        Guid approvalId, DecideApproval request, ServiceDbContext db,
        AuthSellerDirectoryService directory, IIntegrationEventOutboxWriter outbox,
        HttpContext http, CancellationToken ct)
    {
        var reviewer = await ReviewerAsync(directory, http, ct);
        if (reviewer is null) return Results.Forbid();
        var row = await db.Database.SqlQueryRaw<ApprovalRow>(
            """
            SELECT "Id", "RateId", "RevisionNumber", "Reason",
                   "RequestedByUserId", "RequestedAtUtc", "Status",
                   "DecidedByUserId", "DecidedAtUtc", "DecisionReason"
            FROM pricing."ExpiredRateUpdateApprovals" WHERE "Id" = {0}
            """, approvalId).SingleOrDefaultAsync(ct);
        if (row is null) return Results.NotFound();
        var rate = await db.RateHeaders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == row.RateId && !x.IsDeleted, ct);
        if (rate is null) return Results.NotFound();
        if (row.RevisionNumber != rate.RevisionNumber
            || !ExpiredRateUpdateApprovalPolicy.RequiresApproval(rate, DateTimeOffset.UtcNow))
            return Results.Conflict(new { code = "Pricing.ExpiryApprovalStale",
                message = "La tarifa cambió de revisión o dejó de estar vencida." });
        var decisionReason = request.Reason?.Trim();
        if (!request.Approve && (decisionReason is null || decisionReason.Length < 5))
            return Results.BadRequest(new { code = "Pricing.RejectionReasonRequired",
                message = "Debe justificar el rechazo de la solicitud." });
        if (decisionReason?.Length > 4000) return Results.BadRequest();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = request.Approve ? "Approved" : "Rejected";
        var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE pricing."ExpiredRateUpdateApprovals"
               SET "Status" = {status}, "DecidedByUserId" = {reviewer.UserId},
                   "DecidedAtUtc" = now(), "DecisionReason" = {decisionReason}
             WHERE "Id" = {approvalId} AND "Status" = 'Pending'
               AND "RevisionNumber" = {rate.RevisionNumber}
            """, ct);
        if (updated != 1) return Results.Conflict(new {
            code = "Pricing.ExpiredUpdateAlreadyDecided",
            message = "Esta solicitud ya fue atendida."
        });

        var users = await directory.GetPricingUsersAsync(ct);
        var requester = users.FirstOrDefault(u => u.UserId == row.RequestedByUserId)
            ?? new SellerDirectoryUser(row.RequestedByUserId, null, null, null);
        var quote = rate.QuoNumber ?? rate.RateCode;
        var subject = (request.Approve ? "Aprobada" : "Rechazada")
            + " la actualización de tarifa vencida " + quote;
        var body = "La solicitud para actualizar " + quote + " fue "
            + (request.Approve ? "aprobada" : "rechazada")
            + " por " + (reviewer.DisplayName ?? reviewer.Email) + "."
            + (string.IsNullOrWhiteSpace(decisionReason) ? "" : " Observación: " + decisionReason);
        var route = "/pricing/rates?status=Expired";
        await QueueAsync(outbox, "System", requester, rate.Id, approvalId,
            "pricing.rate.expired-update.decided", subject, body, route, ct);
        if (!string.IsNullOrWhiteSpace(requester.Email))
            await QueueAsync(outbox, "Email", requester, rate.Id, approvalId,
                "pricing.rate.expired-update.decided", subject,
                "<p>" + WebUtility.HtmlEncode(body) + "</p>", route, ct);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(new { approvalId, status, rateId = rate.Id });
    }

    // PostgreSQL Outbox correlation_id is varchar(100). Independent channel,
    // target and event marker guarantee retries remain idempotent.
    public static string BuildCorrelationId(Guid approvalId, Guid userId,
        string channel, string notificationType)
        => "pex:" + approvalId.ToString("N")
           + ":" + userId.ToString("N")
           + ":" + (channel == "System" ? "s" : "e")
           + ":" + (notificationType.EndsWith(".requested", StringComparison.Ordinal) ? "r" : "d");

    private static Task QueueAsync(
        IIntegrationEventOutboxWriter outbox, string channel, SellerDirectoryUser target,
        Guid rateId, Guid approvalId, string notificationType, string subject, string body,
        string route, CancellationToken ct)
    {
        var address = channel == "System" ? target.UserId.ToString("D") : target.Email!;
        return outbox.WriteAsync(Event, Event, new
        {
            notificationType, channel,
            entityType = "RateHeader", entityId = rateId.ToString("D"),
            subject, body,
            payload = new { rateId, approvalId, route, action = "open-rate" },
            maxAttempts = 5,
            recipients = new[] { new {
                userId = target.UserId.ToString("D"),
                address,
                displayName = target.DisplayName,
            }},
        },
        correlationId: BuildCorrelationId(approvalId, target.UserId, channel, notificationType),
        cancellationToken: ct);
    }
}
