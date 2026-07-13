namespace Dhole.Pricing.Contracts.Rates.Request;

public sealed record DeleteRateBatchRequest(IReadOnlyCollection<Guid> Ids);
