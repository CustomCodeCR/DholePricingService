namespace Dhole.Pricing.Contracts.Imports.Request;

public sealed record ApproveImportRateBatchRequest(IReadOnlyCollection<Guid> Ids);
