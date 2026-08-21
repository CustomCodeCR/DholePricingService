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
            item.Code,
            item.Slug,
            item.Name,
            item.Value
        );
    }

    private static PricingConfigCatalogItem? MapActive(CatalogItemGrpcResponse response)
    {
        if (!response.Found || response.Item is null)
            return null;

        return MapActive(response.Item);
    }
}
