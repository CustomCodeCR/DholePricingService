namespace Dhole.Pricing.Contracts.Rates.Request;

public sealed record SetRateStatusRequest(
    string Status,
    string? Reason = null,
    string? IdtraNumber = null,
    string? OtherReferences = null,
    string? Observations = null,
    string? DeliveryTarget = null,
    Guid? ContactId = null,
    Guid? LinkedAcceptedRateId = null
);
