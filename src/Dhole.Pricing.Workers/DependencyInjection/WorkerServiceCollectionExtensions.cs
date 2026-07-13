using CustomCodeFramework.Messaging.DependencyInjection;
using CustomCodeFramework.Messaging.Outbox.DependencyInjection;
using CustomCodeFramework.Mongo.DependencyInjection;
using CustomCodeFramework.Redis.DependencyInjection;
using CustomCodeFramework.Redis.Streams.DependencyInjection;
using CustomCodeFramework.Workers.DependencyInjection;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Mongo;
using Dhole.Pricing.Infrastructure.Cache;
using Dhole.Pricing.Infrastructure.Mongo;
using Dhole.Pricing.Worker.Outbox;
using Dhole.Pricing.Worker.Streams;
using Dhole.Pricing.Worker.Workers;

namespace Dhole.Pricing.Worker.DependencyInjection;

public static class WorkerServiceCollectionExtensions
{
    public static IServiceCollection AddPricingWorker(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddPricingRedis(configuration);
        services.AddPricingMongo(configuration);
        services.AddPricingCacheServices();
        services.AddPricingMongoSnapshotWriters();

        services.AddPricingMessaging(configuration);
        services.AddPricingStreamHandlers();
        services.AddPricingPeriodicWorkers(configuration);

        return services;
    }

    private static IServiceCollection AddPricingRedis(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddCustomCodeRedis(configuration);
        services.AddCustomCodeRedisStreams(configuration);

        return services;
    }

    private static IServiceCollection AddPricingMongo(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddCustomCodeMongo(configuration);

        return services;
    }

    private static IServiceCollection AddPricingCacheServices(this IServiceCollection services)
    {
        services.AddScoped<ICostCacheService, CostCacheService>();

        services.AddScoped<IImportRateCacheService, ImportRateCacheService>();

        services.AddScoped<IRateHeaderCacheService, RateHeaderCacheService>();

        return services;
    }

    private static IServiceCollection AddPricingMongoSnapshotWriters(
        this IServiceCollection services
    )
    {
        services.AddScoped<ICostChangeSnapshotWriter, CostChangeSnapshotWriter>();

        services.AddScoped<IImportFclRateChangeSnapshotWriter, ImportFclRateChangeSnapshotWriter>();

        services.AddScoped<IRateDetailChangeSnapshotWriter, RateDetailChangeSnapshotWriter>();

        services.AddScoped<IRateHeaderChangeSnapshotWriter, RateHeaderChangeSnapshotWriter>();

        return services;
    }

    private static IServiceCollection AddPricingMessaging(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddCustomCodeMessaging(configuration);
        services.AddCustomCodeMessagingOutbox(configuration);

        services.AddCustomCodeOutboxProcessor<OutboxProcessor>();

        services.AddCustomCodeInboxProcessor<InboxProcessor>();

        services.AddCustomCodeMessagingOutboxHostedServices();
        services.AddCustomCodeRedisStreamConsumerBackgroundService();

        return services;
    }

    private static IServiceCollection AddPricingStreamHandlers(this IServiceCollection services)
    {
        services.AddCostCacheStreamHandlers();
        services.AddImportRateCacheStreamHandlers();
        services.AddRateHeaderCacheStreamHandlers();

        return services;
    }

    private static IServiceCollection AddCostCacheStreamHandlers(this IServiceCollection services)
    {
        services.AddCustomCodeRedisStreamHandler<CostCreatedStreamHandler>();

        services.AddCustomCodeRedisStreamHandler<CostUpdatedStreamHandler>();

        services.AddCustomCodeRedisStreamHandler<CostDeletedStreamHandler>();

        services.AddCustomCodeRedisStreamHandler<CostActivatedStreamHandler>();

        services.AddCustomCodeRedisStreamHandler<CostInactivatedStreamHandler>();

        return services;
    }

    private static IServiceCollection AddImportRateCacheStreamHandlers(
        this IServiceCollection services
    )
    {
        services.AddCustomCodeRedisStreamHandler<ImportFclRateCreatedStreamHandler>();

        services.AddCustomCodeRedisStreamHandler<ImportFclRateApprovedStreamHandler>();

        services.AddCustomCodeRedisStreamHandler<ImportFclRateRejectedStreamHandler>();

        services.AddCustomCodeRedisStreamHandler<ImportFclRateCreatedAsRateStreamHandler>();

        services.AddCustomCodeRedisStreamHandler<ImportFclRateDeletedStreamHandler>();

        return services;
    }

    private static IServiceCollection AddRateHeaderCacheStreamHandlers(
        this IServiceCollection services
    )
    {
        services.AddCustomCodeRedisStreamHandler<RateHeaderCreatedStreamHandler>();

        services.AddCustomCodeRedisStreamHandler<RateHeaderUpdatedStreamHandler>();

        services.AddCustomCodeRedisStreamHandler<RateHeaderDeletedStreamHandler>();

        services.AddCustomCodeRedisStreamHandler<RateHeaderAmountsChangedStreamHandler>();

        services.AddCustomCodeRedisStreamHandler<RateHeaderApprovedStreamHandler>();

        services.AddCustomCodeRedisStreamHandler<RateHeaderRejectedStreamHandler>();

        services.AddCustomCodeRedisStreamHandler<RateDetailAddedStreamHandler>();

        services.AddCustomCodeRedisStreamHandler<RateDetailUpdatedStreamHandler>();

        services.AddCustomCodeRedisStreamHandler<RateDetailRemovedStreamHandler>();

        return services;
    }

    private static IServiceCollection AddPricingPeriodicWorkers(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddCustomCodeWorkers(configuration);

        services.AddCustomCodePeriodicWorker<PricingCacheWarmupWorker>();

        return services;
    }
}
