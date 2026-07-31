using System.Text.Json;
using CustomCodeFramework.Redis.Streams.Messages;

namespace Dhole.Pricing.Worker.Streams;

internal static class PricingExtractionStreamPayloadReader
{
    public static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web
    )
    {
        PropertyNameCaseInsensitive = true,
    };

    public static T Read<T>(RedisStreamEnvelope envelope)
    {
        using var document = JsonDocument.Parse(envelope.PayloadJson);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "payload", "data", "eventData" })
            {
                var property = root
                    .EnumerateObject()
                    .FirstOrDefault(item =>
                        string.Equals(
                            item.Name,
                            name,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    root = property.Value;
                    break;
                }
            }
        }

        return root.Deserialize<T>(JsonOptions)
            ?? throw new InvalidOperationException(
                $"El evento '{envelope.MessageType}' no contiene un payload válido."
            );
    }
}
