namespace Dhole.Pricing.Application.Abstractions.Mongo;

public interface IRateDetailChangeSnapshotWriter
{
    Task WriteAsync(
        Guid eventId,
        string eventName,
        string entityType,
        Guid entityId,
        Guid rateHeaderId,
        Guid? costId,
        string name,
        string costDetailType,
        string costType,
        Guid currencyId,
        string currencyNameSnapshot,
        string currencyCodeSnapshot,
        decimal costAmount,
        decimal saleAmount,
        decimal utilityAmount,
        string? notes,
        string action,
        string? previousValueJson,
        string? newValueJson,
        Guid? changedBy,
        DateTime changedAtUtc,
        Guid? correlationId,
        CancellationToken cancellationToken = default
    );
}
