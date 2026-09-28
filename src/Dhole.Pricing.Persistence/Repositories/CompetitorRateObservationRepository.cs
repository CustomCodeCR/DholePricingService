using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.Repositories;

public sealed class CompetitorRateObservationRepository(ServiceDbContext dbContext)
    : EfRepository<CompetitorRateObservation, Guid>(dbContext),
        ICompetitorRateObservationRepository
{
    public async Task<IReadOnlyCollection<CompetitorRateObservation>> GetMarketCandidatesAsync(
        ShipmentMode mode,
        Guid polId,
        DateTime referenceDate,
        DateTime oldestValidTo,
        CancellationToken cancellationToken = default
    )
    {
        var referenceUtc = NormalizeUtc(referenceDate);
        var oldestUtc = NormalizeUtc(oldestValidTo);

        return await dbContext.CompetitorRateObservations
            .AsNoTracking()
            .Where(x =>
                x.Mode == mode
                && x.PolId == polId
                && x.ValidFrom <= referenceUtc
                && x.ValidTo >= oldestUtc
            )
            .OrderByDescending(x => x.ValidTo)
            .ThenByDescending(x => x.ValidFrom)
            .ThenByDescending(x => x.ImportedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<CompetitorRateObservation>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default
    )
    {
        if (ids.Count == 0)
            return Array.Empty<CompetitorRateObservation>();

        var distinct = ids.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (distinct.Length == 0)
            return Array.Empty<CompetitorRateObservation>();

        return await dbContext.CompetitorRateObservations
            .AsNoTracking()
            .Where(x => distinct.Contains(x.Id))
            .ToListAsync(cancellationToken);
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
