using CustomCodeFramework.Redis.Streams.Abstractions;
using CustomCodeFramework.Redis.Streams.Messages;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Worker.Streams;

/// <summary>
/// Operations requests become durable Pricing work items, not implicit
/// quotation price/cost changes. Idempotency is the Operations request UUID.
/// </summary>
internal sealed class OperationsPricingReviewRequestedStreamHandler(
    ServiceDbContext db,
    ILogger<OperationsPricingReviewRequestedStreamHandler> logger)
    : IRedisStreamMessageHandler
{
    public string MessageType => "operations.pricing-review.requested.v1";

    private sealed record Request(
        Guid CaseId, string Idtra, Guid CompanyId, Guid ReviewRequestId,
        Guid ServiceId, string ServiceCode, string Reason, Guid RequestedBy,
        Guid? QuoteId, Guid? AcceptedOptionId, DateTimeOffset? RequestedAtUtc);

    public async Task HandleAsync(RedisStreamEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        var message = PricingExtractionStreamPayloadReader.Read<Request>(envelope);
        if (message.CaseId == Guid.Empty || message.CompanyId == Guid.Empty ||
            message.ReviewRequestId == Guid.Empty || message.ServiceId == Guid.Empty ||
            message.RequestedBy == Guid.Empty || !message.Idtra.StartsWith("IDTRA",
                StringComparison.Ordinal) || message.Idtra.Length > 32 ||
            string.IsNullOrWhiteSpace(message.ServiceCode) ||
            message.ServiceCode.Length > 60 ||
            string.IsNullOrWhiteSpace(message.Reason) ||
            message.Reason.Length is < 10 or > 2000)
            throw new InvalidOperationException("Operations commercial review event is invalid.");

        if (await db.OperationsReviewRequests.AsNoTracking()
            .AnyAsync(x => x.OperationsRequestId == message.ReviewRequestId,
                cancellationToken))
            return;

        if (message.AcceptedOptionId.HasValue &&
            !await db.RateHeaders.AsNoTracking().AnyAsync(
                x => x.Id == message.AcceptedOptionId.Value && !x.IsDeleted,
                cancellationToken))
            throw new InvalidOperationException("Pricing cannot resolve accepted option for review.");

        var review = new PricingOperationsReviewRequest
        {
            OperationsRequestId = message.ReviewRequestId,
            CaseId = message.CaseId, Idtra = message.Idtra,
            CompanyId = message.CompanyId, ServiceId = message.ServiceId,
            ServiceCode = message.ServiceCode.Trim().ToUpperInvariant(),
            QuoteId = message.QuoteId,
            AcceptedOptionId = message.AcceptedOptionId,
            Reason = message.Reason.Trim(), RequestedBy = message.RequestedBy,
            RequestedAtUtc = message.RequestedAtUtc ?? DateTimeOffset.UtcNow
        };
        db.OperationsReviewRequests.Add(review);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (await db.OperationsReviewRequests.AsNoTracking().AnyAsync(
                x => x.OperationsRequestId == message.ReviewRequestId,
                cancellationToken))
                return;
            throw;
        }
        logger.LogInformation(
            "Operations Pricing review request {RequestId} persisted for {Idtra}",
            message.ReviewRequestId, message.Idtra);
    }
}
