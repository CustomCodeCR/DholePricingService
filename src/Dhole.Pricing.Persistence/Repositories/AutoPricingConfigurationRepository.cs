using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Domain.MarketPricing.Entities;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.Repositories;

public sealed class AutoPricingConfigurationRepository(ServiceDbContext dbContext)
    : IAutoPricingConfigurationRepository
{
    public Task<AutoPricingProfile?> GetActiveProfileAsync(
        string? code = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = dbContext.AutoPricingProfiles
            .AsNoTracking()
            .Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(code))
        {
            var normalized = code.Trim().ToUpperInvariant();
            return query.FirstOrDefaultAsync(x => x.Code == normalized, cancellationToken);
        }

        return query
            .OrderBy(x => x.Code == "STANDARD" ? 0 : 1)
            .ThenBy(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<ChargePricingRule>> GetActiveChargeRulesAsync(
        CancellationToken cancellationToken = default
    ) =>
        await dbContext.ChargePricingRules
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.AdjustmentPriority)
            .ThenBy(x => x.ChargeCode)
            .ToListAsync(cancellationToken);
}
