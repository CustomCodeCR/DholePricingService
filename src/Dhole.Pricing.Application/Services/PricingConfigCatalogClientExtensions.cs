using Dhole.Pricing.Application.Abstractions.Services;

namespace Dhole.Pricing.Application.Services;

/// <summary>
/// Centraliza la validación de referencias de Pricing contra Dhole.Config.
/// Pricing nunca debe confiar en los snapshots (Name/Code) enviados por el cliente:
/// el Id es la referencia y Config es la fuente de verdad.
/// </summary>
public static class PricingConfigCatalogClientExtensions
{
    public static async Task<PricingConfigCatalogItem?> GetActiveInGroupAsync(
        this IPricingConfigCatalogClient client,
        Guid? catalogItemId,
        string expectedGroupSlug,
        CancellationToken cancellationToken = default)
    {
        if (!catalogItemId.HasValue || catalogItemId.Value == Guid.Empty)
            return null;

        var item = await client.GetActiveByIdAsync(catalogItemId.Value, cancellationToken);
        return item is not null
            && item.CatalogGroupSlug.Equals(expectedGroupSlug, StringComparison.OrdinalIgnoreCase)
                ? item
                : null;
    }

    public static async Task<PricingConfigCatalogItem?> GetActiveInAnyGroupAsync(
        this IPricingConfigCatalogClient client,
        Guid? catalogItemId,
        IReadOnlyCollection<string> acceptedGroupSlugs,
        CancellationToken cancellationToken = default)
    {
        if (!catalogItemId.HasValue || catalogItemId.Value == Guid.Empty)
            return null;

        var item = await client.GetActiveByIdAsync(catalogItemId.Value, cancellationToken);
        return item is not null
            && acceptedGroupSlugs.Contains(item.CatalogGroupSlug, StringComparer.OrdinalIgnoreCase)
                ? item
                : null;
    }

    public static string SnapshotName(this PricingConfigCatalogItem item, bool preferValue = false)
    {
        if (preferValue && !string.IsNullOrWhiteSpace(item.Value))
            return item.Value.Trim();

        return item.Name.Trim();
    }
}
