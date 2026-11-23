using System.Text.Json;
using CustomCodeFramework.Redis.Streams.Abstractions;
using CustomCodeFramework.Redis.Streams.Messages;
using Dhole.Pricing.Persistence.DbContexts;
using Dhole.Pricing.Persistence.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Worker.Streams;

/// <summary>
/// Consume a durable, idempotent Operations discrepancy. Pricing decides if an
/// additional service changes a commercial offer; this handler never approves it.
/// </summary>
internal sealed class OperationsPricingReviewRequestedStreamHandler(
    ServiceDbContext db,
    ILogger<OperationsPricingReviewRequestedStreamHandler> logger)
    : IRedisStreamMessageHandler
{
    public string MessageType => "operations.pricing.review-requested.v1";

    private sealed record Event(
        int SchemaVersion, Guid ReviewRequestId, Guid CaseId,
        Guid? QuoteId, Guid? QuoteGroupId, Guid ServiceCatalogId,
        string ServiceCode, string ServiceName, string Reason,
        Guid RequestedBy, DateTimeOffset RequestedAtUtc);

    public async Task HandleAsync(RedisStreamEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Deserialize<Event>(envelope.PayloadJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new JsonException("Operations review event is empty.");
        if (body.SchemaVersion != 1 || body.ReviewRequestId == Guid.Empty ||
            body.CaseId == Guid.Empty || body.ServiceCatalogId == Guid.Empty ||
            string.IsNullOrWhiteSpace(body.ServiceName) ||
            string.IsNullOrWhiteSpace(body.Reason) || body.RequestedBy == Guid.Empty)
            throw new InvalidOperationException("Invalid Operations commercial review contract.");
        if (await db.Set<PricingOperationsServiceReview>().AsNoTracking()
            .AnyAsync(x => x.Id == body.ReviewRequestId, cancellationToken)) return;

        db.Set<PricingOperationsServiceReview>().Add(new PricingOperationsServiceReview {
            Id = body.ReviewRequestId, CaseId = body.CaseId,
            QuoteId = body.QuoteId, QuoteGroupId = body.QuoteGroupId,
            ServiceCatalogId = body.ServiceCatalogId,
            ServiceCode = body.ServiceCode, ServiceName = body.ServiceName,
            Reason = body.Reason, RequestedBy = body.RequestedBy,
            RequestedAtUtc = body.RequestedAtUtc,
        });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException
            { SqlState: "23505" })
        {
            logger.LogInformation("Pricing review {ReviewId} already registered",
                body.ReviewRequestId);
            db.ChangeTracker.Clear();
        }
    }
}
