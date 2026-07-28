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

            if (!response.Found || response.Item is null || !response.Item.IsActive)
            {
                return null;
            }

            if (!Guid.TryParse(response.Item.Id, out var id))
            {
                throw new InvalidOperationException(
                    "Config devolvió un identificador de catálogo inválido."
                );
            }

            return new PricingConfigCatalogItem(
                id,
                response.Item.CatalogGroupSlug,
                response.Item.Code,
                response.Item.Slug,
                response.Item.Name
            );
        }
        catch (RpcException exception)
        {
            throw new InvalidOperationException(
                $"Config.{exception.StatusCode}: {exception.Status.Detail}",
                exception
            );
        }
    }
}
