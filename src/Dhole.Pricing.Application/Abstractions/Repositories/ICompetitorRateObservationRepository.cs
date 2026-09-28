using CustomCodeFramework.Persistence.Abstractions;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.Abstractions.Repositories;

public interface ICompetitorRateObservationRepository
    : IRepository<CompetitorRateObservation, Guid>
{
    Task<IReadOnlyCollection<CompetitorRateObservation>> GetMarketCandidatesAsync(
        ShipmentMode mode,
        Guid polId,
        DateTime referenceDate,
        DateTime oldestValidTo,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<CompetitorRateObservation>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default
    );
}
