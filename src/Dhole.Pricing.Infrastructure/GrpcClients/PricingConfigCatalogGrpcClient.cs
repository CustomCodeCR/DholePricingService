using System.Globalization;
using System.Text;
using Dhole.Config.Contracts.Grpc;
using Dhole.Pricing.Application.Abstractions.Services;
using Grpc.Core;

namespace Dhole.Pricing.Infrastructure.GrpcClients;

public sealed class PricingConfigCatalogGrpcClient(
    ConfigCatalogGrpc.ConfigCatalogGrpcClient client
) : IPricingConfigCatalogClient
{
    public async Task<PricingConfigCatalogItem?> GetActiveByIdAsync(
        Guid catalogItemId,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var response = await client.GetCatalogItemByIdAsync(
                new GetCatalogItemByIdGrpcRequest
                {
                    CatalogItemId = catalogItemId.ToString(),
                },
                cancellationToken: cancellationToken
            );

            return MapActive(response);
        }
        catch (RpcException exception)
        {
            throw new InvalidOperationException(
                $"Config.{exception.StatusCode}: {exception.Status.Detail}",
                exception
            );
        }
    }

    public async Task<PricingConfigCatalogItem?> GetActiveByCodeAsync(
        string catalogGroupSlug,
        string catalogItemCode,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(catalogGroupSlug) || string.IsNullOrWhiteSpace(catalogItemCode))
            return null;

        try
        {
            var response = await client.GetCatalogItemByCodeAsync(
                new GetCatalogItemByCodeGrpcRequest
                {
                    CatalogGroupSlug = catalogGroupSlug.Trim(),
                    CatalogItemCode = catalogItemCode.Trim(),
                },
                cancellationToken: cancellationToken
            );

            return MapActive(response);
        }
        catch (RpcException exception)
        {
            throw new InvalidOperationException(
                $"Config.{exception.StatusCode}: {exception.Status.Detail}",
                exception
            );
        }
    }

    public async Task<IReadOnlyCollection<PricingConfigCatalogItem>> GetActiveByGroupAsync(
        string catalogGroupSlug,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(catalogGroupSlug))
            return Array.Empty<PricingConfigCatalogItem>();

        try
        {
            var response = await client.GetActiveCatalogItemsByGroupAsync(
                new GetActiveCatalogItemsByGroupGrpcRequest
                {
                    CatalogGroupSlug = catalogGroupSlug.Trim(),
                },
                cancellationToken: cancellationToken
            );

            return response.Items
                .Where(item => item.IsActive)
                .Select(MapActive)
                .Where(item => item is not null)
                .Cast<PricingConfigCatalogItem>()
                .ToArray();
        }
        catch (RpcException exception)
        {
            throw new InvalidOperationException(
                $"Config.{exception.StatusCode}: {exception.Status.Detail}",
                exception
            );
        }
    }

    private static PricingConfigCatalogItem? MapActive(CatalogItemGrpcModel item)
    {
        if (!item.IsActive) return null;

        if (!Guid.TryParse(item.Id, out var id))
            throw new InvalidOperationException("Config devolvió un identificador de catálogo inválido.");

        return new PricingConfigCatalogItem(
            id,
            item.CatalogGroupSlug,
            ResolveBusinessCode(item),
            item.Slug,
            item.Name,
            item.Value,
            string.IsNullOrWhiteSpace(item.MetadataJson) ? null : item.MetadataJson
        );
    }

    private static string ResolveBusinessCode(CatalogItemGrpcModel item)
    {
        if (!string.Equals(item.CatalogGroupSlug, "currencies", StringComparison.OrdinalIgnoreCase))
            return item.Code;

        // Config conserva identificadores administrativos como CUR-2026-001 o MON-2026-001
        // en Code. Pricing necesita el código financiero (USD/CRC) para conversiones,
        // totales y margen. La identidad de catálogo sigue estando preservada por Id.
        foreach (var candidate in new[] { item.Value, item.Name, item.Slug, item.Code })
        {
            var canonical = TryResolveCurrencyCode(candidate);
            if (canonical is not null)
                return canonical;
        }

        return item.Code;
    }

    private static string? TryResolveCurrencyCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var raw = value.Trim().ToUpperInvariant();
        if (raw is "USD" or "CRC")
            return raw;
        if (raw == "$" || raw == "US$")
            return "USD";
        if (raw == "₡")
            return "CRC";

        var decomposed = raw.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        }

        var normalized = string.Join(
            ' ',
            builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
        );

        if (normalized.Contains("US DOLLAR", StringComparison.Ordinal)
            || normalized.Contains("DOLLAR", StringComparison.Ordinal)
            || normalized.Contains("DOLAR", StringComparison.Ordinal))
            return "USD";

        if (normalized.Contains("COSTA RICAN COLON", StringComparison.Ordinal)
            || normalized.Contains("COLON COSTARRICENSE", StringComparison.Ordinal)
            || normalized.Contains("COLON", StringComparison.Ordinal))
            return "CRC";

        return null;
    }

    private static PricingConfigCatalogItem? MapActive(CatalogItemGrpcResponse response)
    {
        if (!response.Found || response.Item is null)
            return null;

        return MapActive(response.Item);
    }
}
