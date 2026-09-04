using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dhole.Pricing.Contracts.Costs;

/// <summary>
/// The cost domain historically stores a null shipment mode as the wildcard that
/// applies to every shipment mode. The public Costs contract exposes that wildcard
/// explicitly as "Any" while preserving null internally for backwards compatibility.
/// </summary>
public sealed class ShipmentModeJsonConverter : JsonConverter<string?>
{
    public override bool HandleNull => true;

    public override string? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        var value = reader.GetString();
        return string.Equals(value, "Any", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;
    }

    public override void Write(
        Utf8JsonWriter writer,
        string? value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(string.IsNullOrWhiteSpace(value) ? "Any" : value);
    }
}
