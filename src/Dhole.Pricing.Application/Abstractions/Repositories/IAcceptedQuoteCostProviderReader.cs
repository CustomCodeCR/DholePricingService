namespace Dhole.Pricing.Application.Abstractions.Repositories;

/// <summary>The actual supplier behind a catalog cost, without guessing from the QUO header.</summary>
public sealed record AcceptedQuoteCostProvider(Guid? SupplierId, string? SupplierName);

public interface IAcceptedQuoteCostProviderReader
{
    Task<IReadOnlyDictionary<Guid, AcceptedQuoteCostProvider>> ResolveAsync(
        IReadOnlyCollection<Guid> costIds, CancellationToken cancellationToken = default);
}
