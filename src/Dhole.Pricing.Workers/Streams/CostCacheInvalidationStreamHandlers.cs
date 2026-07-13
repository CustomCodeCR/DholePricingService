using Dhole.Pricing.Application.Abstractions.Cache;

namespace Dhole.Pricing.Worker.Streams;

internal sealed class CostCreatedStreamHandler(
    ICostCacheService cache,
    ILogger<CostCreatedStreamHandler> logger
) : PricingCostCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "pricing.cost.created";
}

internal sealed class CostUpdatedStreamHandler(
    ICostCacheService cache,
    ILogger<CostUpdatedStreamHandler> logger
) : PricingCostCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "pricing.cost.updated";
}

internal sealed class CostDeletedStreamHandler(
    ICostCacheService cache,
    ILogger<CostDeletedStreamHandler> logger
) : PricingCostCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "pricing.cost.deleted";
}

internal sealed class CostActivatedStreamHandler(
    ICostCacheService cache,
    ILogger<CostActivatedStreamHandler> logger
) : PricingCostCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "pricing.cost.activated";
}

internal sealed class CostInactivatedStreamHandler(
    ICostCacheService cache,
    ILogger<CostInactivatedStreamHandler> logger
) : PricingCostCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "pricing.cost.inactivated";
}
