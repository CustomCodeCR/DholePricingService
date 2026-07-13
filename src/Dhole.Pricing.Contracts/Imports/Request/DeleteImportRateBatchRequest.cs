namespace Dhole.Pricing.Contracts.Imports.Request;

public sealed record DeleteImportRateBatchRequest(IReadOnlyCollection<Guid> Ids);
