namespace Dhole.Pricing.Contracts.Rates.Request;

public sealed record CreateCarrierFreeDayRuleRequest(
    Guid CarrierId,
    string CarrierName,
    string CarrierCode,
    int FreeDays,
    bool IsActive = true
);

public sealed record UpdateCarrierFreeDayRuleRequest(
    Guid CarrierId,
    string CarrierName,
    string CarrierCode,
    int FreeDays,
    bool IsActive
);

public sealed record RateTermBlockItemRequest(
    Guid RateTermItemId,
    string Category,
    int SortOrder = 0
);

public sealed record UpsertRateTermBlockRequest(
    string Name,
    string? RateType,
    string? ShipmentMode,
    Guid? PoeId,
    string? PoeName,
    string? PoeCode,
    Guid? IncotermId,
    string? IncotermName,
    string? IncotermCode,
    int SortOrder,
    bool IsActive,
    IReadOnlyCollection<RateTermBlockItemRequest> Items
);
