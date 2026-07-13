using Dhole.Pricing.Application.Abstractions.Cache;

namespace Dhole.Pricing.Worker.Streams;

internal sealed class RateHeaderCreatedStreamHandler(
    IRateHeaderCacheService cache,
    ILogger<RateHeaderCreatedStreamHandler> logger
) : PricingRateHeaderCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "pricing.rate-header.created";
}

internal sealed class RateHeaderUpdatedStreamHandler(
    IRateHeaderCacheService cache,
    ILogger<RateHeaderUpdatedStreamHandler> logger
) : PricingRateHeaderCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "pricing.rate-header.updated";
}

internal sealed class RateHeaderDeletedStreamHandler(
    IRateHeaderCacheService cache,
    ILogger<RateHeaderDeletedStreamHandler> logger
) : PricingRateHeaderCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "pricing.rate-header.deleted";
}

internal sealed class RateHeaderAmountsChangedStreamHandler(
    IRateHeaderCacheService cache,
    ILogger<RateHeaderAmountsChangedStreamHandler> logger
) : PricingRateHeaderCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "pricing.rate-header.amounts-changed";
}

internal sealed class RateHeaderApprovedStreamHandler(
    IRateHeaderCacheService cache,
    ILogger<RateHeaderApprovedStreamHandler> logger
) : PricingRateHeaderCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "pricing.rate-header.approved";
}

internal sealed class RateHeaderRejectedStreamHandler(
    IRateHeaderCacheService cache,
    ILogger<RateHeaderRejectedStreamHandler> logger
) : PricingRateHeaderCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "pricing.rate-header.rejected";
}
