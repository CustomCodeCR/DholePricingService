using Dhole.Pricing.Application.Abstractions.Cache;

namespace Dhole.Pricing.Worker.Streams;

internal sealed class ImportFclRateCreatedStreamHandler(
    IImportRateCacheService cache,
    ILogger<ImportFclRateCreatedStreamHandler> logger
) : PricingImportRateCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "pricing.import-fcl-rate.created";
}

internal sealed class ImportFclRateApprovedStreamHandler(
    IImportRateCacheService cache,
    ILogger<ImportFclRateApprovedStreamHandler> logger
) : PricingImportRateCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "pricing.import-fcl-rate.approved";
}

internal sealed class ImportFclRateRejectedStreamHandler(
    IImportRateCacheService cache,
    ILogger<ImportFclRateRejectedStreamHandler> logger
) : PricingImportRateCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "pricing.import-fcl-rate.rejected";
}

internal sealed class ImportFclRateCreatedAsRateStreamHandler(
    IImportRateCacheService cache,
    ILogger<ImportFclRateCreatedAsRateStreamHandler> logger
) : PricingImportRateCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "pricing.import-fcl-rate.created-as-rate";
}

internal sealed class ImportFclRateDeletedStreamHandler(
    IImportRateCacheService cache,
    ILogger<ImportFclRateDeletedStreamHandler> logger
) : PricingImportRateCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "pricing.import-fcl-rate.deleted";
}
