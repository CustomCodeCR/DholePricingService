using System.Text.Json;
using CustomCodeFramework.Redis.Streams.Abstractions;
using CustomCodeFramework.Redis.Streams.Messages;
using Dhole.Pricing.Application.Abstractions.Cache;

namespace Dhole.Pricing.Worker.Streams;

internal abstract class PricingCostCacheInvalidationStreamHandlerBase(
    ICostCacheService cache,
    ILogger logger
) : IRedisStreamMessageHandler
{
    public abstract string MessageType { get; }

    public async Task HandleAsync(
        RedisStreamEnvelope envelope,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            using var document = JsonDocument.Parse(envelope.PayloadJson);

            var costId = TryGetGuid(document.RootElement, "costId", "entityId", "id");

            if (!costId.HasValue)
            {
                throw new InvalidOperationException(
                    $"El evento '{envelope.MessageType}' no contiene "
                        + "un identificador de costo válido."
                );
            }

            await cache.RemoveCostCacheAsync(costId.Value, cancellationToken);

            logger.LogInformation(
                "Pricing cost cache invalidated. "
                    + "MessageType: {MessageType}, "
                    + "MessageId: {MessageId}, "
                    + "CostId: {CostId}.",
                envelope.MessageType,
                envelope.MessageId,
                costId.Value
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to invalidate pricing cost cache. "
                    + "MessageType: {MessageType}, "
                    + "MessageId: {MessageId}.",
                envelope.MessageType,
                envelope.MessageId
            );

            throw;
        }
    }

    private static Guid? TryGetGuid(JsonElement root, params string[] propertyNames)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Guid.TryParse(property.Value.ToString(), out var guid))
                {
                    return guid;
                }
            }
        }

        foreach (var containerName in new[] { "payload", "data", "eventData" })
        {
            foreach (var property in root.EnumerateObject())
            {
                if (
                    !string.Equals(property.Name, containerName, StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                var guid = TryGetGuid(property.Value, propertyNames);

                if (guid.HasValue)
                {
                    return guid;
                }
            }
        }

        return null;
    }
}
