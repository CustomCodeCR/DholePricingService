namespace Dhole.Pricing.Contracts.Rates.Response;

public sealed record CarrierFreeDayRuleDto(
    Guid Id,
    Guid CarrierId,
    string CarrierName,
    string CarrierCode,
    int FreeDays,
    bool IsActive
);

public sealed record RateTermBlockItemDto(
    Guid RateTermItemId,
    string Text,
    string Category,
    int SortOrder
);

public sealed record RateTermBlockDto(
    Guid Id,
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
    IReadOnlyCollection<RateTermBlockItemDto> Items
);
