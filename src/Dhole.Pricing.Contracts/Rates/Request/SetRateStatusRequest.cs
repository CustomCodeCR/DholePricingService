namespace Dhole.Pricing.Contracts.Rates.Request;

public sealed record SetRateStatusRequest(
    string Status,
    string? Reason = null,
    string? IdtraNumber = null
);
