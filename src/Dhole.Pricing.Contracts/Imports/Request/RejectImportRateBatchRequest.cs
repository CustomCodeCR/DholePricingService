namespace Dhole.Pricing.Contracts.Imports.Request;

public sealed record RejectImportRateBatchRequest(IReadOnlyCollection<Guid> Ids, string Reason);
