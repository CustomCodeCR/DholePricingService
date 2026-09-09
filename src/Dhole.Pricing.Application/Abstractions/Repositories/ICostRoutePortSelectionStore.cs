namespace Dhole.Pricing.Application.Abstractions.Repositories;

public sealed record CostRoutePortSelectionSet(
    IReadOnlyCollection<Guid> PolIds,
    IReadOnlyCollection<Guid> PoeIds,
    IReadOnlyCollection<Guid> PodIds
)
{
    public static CostRoutePortSelectionSet Empty { get; } = new(
        Array.Empty<Guid>(),
        Array.Empty<Guid>(),
        Array.Empty<Guid>()
    );
}

public interface ICostRoutePortSelectionStore
{
    Task<CostRoutePortSelectionSet> GetAsync(
        Guid costId,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyDictionary<Guid, CostRoutePortSelectionSet>> GetManyAsync(
        IReadOnlyCollection<Guid> costIds,
        CancellationToken cancellationToken = default
    );

    Task ReplaceAsync(
        Guid costId,
        IReadOnlyCollection<Guid> polIds,
        IReadOnlyCollection<Guid> poeIds,
        IReadOnlyCollection<Guid> podIds,
        CancellationToken cancellationToken = default
    );
}
