namespace Dhole.Pricing.Application.Abstractions.Mongo;

public interface ICostChangeSnapshotWriter
{
    Task WriteAsync(
        Guid eventId,
        string eventName,
        string entityType,
        Guid entityId,
        string costName,
        string costType,
        string costDetailType,
        Guid? carrierId,
        string? carrierNameSnapshot,
        string? carrierCodeSnapshot,
        Guid? agentId,
        string? agentNameSnapshot,
        string? agentCodeSnapshot,
        Guid portId,
        string portNameSnapshot,
        string portCodeSnapshot,
        string portRole,
        Guid currencyId,
        string currencyNameSnapshot,
        string currencyCodeSnapshot,
        decimal costAmount,
        decimal saleAmount,
        decimal utilityAmount,
        string? notes,
        bool isActive,
        string action,
        string? previousValueJson,
        string? newValueJson,
        Guid? changedBy,
        DateTime changedAtUtc,
        Guid? correlationId,
        CancellationToken cancellationToken = default
    );
}
