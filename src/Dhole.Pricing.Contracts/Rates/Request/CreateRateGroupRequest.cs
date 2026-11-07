namespace Dhole.Pricing.Contracts.Rates.Request;

/// <summary>
/// An all-or-nothing FCL quotation with independent POE/carrier/haulage options.
/// Every Rate is the complete Screen 7 snapshot for its own alternative.
/// </summary>
public sealed record CreateRateGroupAlternativeRequest(
    CreateRateRequest Rate,
    string HaulageMode = "Carrier",
    string? RouteKey = null
);

public sealed record CreateRateGroupRequest(
    IReadOnlyList<CreateRateGroupAlternativeRequest> Alternatives
);
