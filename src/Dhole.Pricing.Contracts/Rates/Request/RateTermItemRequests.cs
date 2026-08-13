namespace Dhole.Pricing.Contracts.Rates.Request;

public sealed record CreateRateTermItemRequest(string Text, int SortOrder = 0);
public sealed record UpdateRateTermItemRequest(string Text, int SortOrder, bool IsActive);

public sealed record SetRateTermItemActiveRequest(bool IsActive);
