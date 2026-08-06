using CustomCodeFramework.Workers.Abstractions;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Worker.Workers;

internal sealed class PricingRateExpirationWorker(
    ServiceDbContext dbContext,
    IConfiguration configuration,
    ILogger<PricingRateExpirationWorker> logger
) : IBackgroundWorker
{
    public string Name => "pricing.rate-expiration";

    public async Task ExecuteAsync(
        IWorkerExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        if (!configuration.GetValue("Pricing:RateExpiration:Enabled", true))
        {
            return;
        }

        var todayUtc = DateTime.UtcNow.Date;

        var rates = await dbContext
            .RateHeaders.Where(rate =>
                !rate.IsDeleted
                && rate.ValidTo < todayUtc
                && rate.Status != RateStatus.Expired
                && rate.Status != RateStatus.Closed
                && rate.Status != RateStatus.RejectedByManagement
                && rate.Status != RateStatus.RejectedByClient
            )
            .ToListAsync(cancellationToken);

        if (rates.Count == 0)
        {
            logger.LogDebug("No hay tarifas vigentes pendientes de marcar como vencidas.");
            return;
        }

        var expiredCount = 0;

        foreach (var rate in rates)
        {
            if (rate.MarkExpired(todayUtc))
            {
                expiredCount++;
            }
        }

        if (expiredCount == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Se marcaron {ExpiredCount} tarifas como vencidas para la fecha UTC {ExpirationDate}.",
            expiredCount,
            todayUtc
        );
    }
}
