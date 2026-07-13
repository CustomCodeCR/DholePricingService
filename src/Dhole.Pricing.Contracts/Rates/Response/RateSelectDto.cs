namespace Dhole.Pricing.Contracts.Rates.Response;

public sealed record RateSelectDto(Guid Id, string Label, string Status, bool IsActive);
