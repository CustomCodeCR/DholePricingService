using Dhole.Pricing.Application.Abstractions.Cache;

namespace Dhole.Pricing.Worker.Streams;

internal sealed class RateDetailAddedStreamHandler(
    IRateHeaderCacheService cache,
    ILogger<RateDetailAddedStreamHandler> logger
) : PricingRateHeaderCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "pricing.rate-detail.added";
}

internal sealed class RateDetailUpdatedStreamHandler(
    IRateHeaderCacheService cache,
    ILogger<RateDetailUpdatedStreamHandler> logger
) : PricingRateHeaderCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "pricing.rate-detail.updated";
}

internal sealed class RateDetailRemovedStreamHandler(
    IRateHeaderCacheService cache,
    ILogger<RateDetailRemovedStreamHandler> logger
) : PricingRateHeaderCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "pricing.rate-detail.removed";
}
