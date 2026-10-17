namespace Dhole.Pricing.Contracts.Competitors;

/// <summary>
/// Explicit commercial route override: each POL becomes one comparable market observation.
/// POD is intentionally absent; this quote terminates at the port of entry.
/// </summary>
public sealed record SetCompetitorTariffRoutesRequest(
    IReadOnlyCollection<Guid> PolIds,
    Guid PoeId = default,
    IReadOnlyCollection<Guid>? PoeIds = null
);
