namespace Dhole.Pricing.Application.Abstractions.Services;

public interface IPricingConfigCatalogClient
{
    Task<PricingConfigCatalogItem?> GetActiveByIdAsync(
        Guid catalogItemId,
        CancellationToken cancellationToken = default
    );

    Task<PricingConfigCatalogItem?> GetActiveByCodeAsync(
        string catalogGroupSlug,
        string catalogItemCode,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<PricingConfigCatalogItem>> GetActiveByGroupAsync(
        string catalogGroupSlug,
        CancellationToken cancellationToken = default
    );
}

public sealed record PricingConfigCatalogItem(
    Guid Id,
    string CatalogGroupSlug,
    string Code,
    string Slug,
    string Name,
    string? Value,
    string? MetadataJson
);
