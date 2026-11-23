namespace Dhole.Pricing.Application.Abstractions.Repositories;

/// <summary>Resolve the assigned seller from a persisted Pricing request, not the accepting actor.</summary>
public interface IAcceptedQuoteExecutiveReader
{
    Task<Guid?> ResolveAsync(Guid rateId, CancellationToken cancellationToken = default);
}
