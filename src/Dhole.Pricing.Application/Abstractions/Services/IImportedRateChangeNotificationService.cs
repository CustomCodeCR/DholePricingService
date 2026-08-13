using Dhole.Pricing.Domain.Imports.Entities;

namespace Dhole.Pricing.Application.Abstractions.Services;

public interface IImportedRateChangeNotificationService
{
    Task QueueVariationNotificationsAsync(
        ImportFclRates currentRate,
        CancellationToken cancellationToken = default
    );
}
