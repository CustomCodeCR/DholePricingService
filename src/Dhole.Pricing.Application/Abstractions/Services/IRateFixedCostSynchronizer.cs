using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.Application.Abstractions.Services;

public interface IRateFixedCostSynchronizer
{
    Task SynchronizeAsync(
        RateHeader rate,
        Guid? updatedBy,
        CancellationToken cancellationToken = default
    );
}
