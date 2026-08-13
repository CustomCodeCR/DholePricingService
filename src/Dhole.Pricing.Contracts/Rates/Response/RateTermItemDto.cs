namespace Dhole.Pricing.Contracts.Rates.Response;

public sealed record RateTermItemDto(
    Guid Id, string Text, int SortOrder, bool IsActive
);
