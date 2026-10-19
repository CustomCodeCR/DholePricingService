using System.Text.Json;

namespace Dhole.Pricing.Api.Endpoints;

// The payload discriminator keeps legacy requests and migrations intact.
internal static class RateModificationRequestPayload
{
    public static bool IsModification(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("kind", out var kind)
                && kind.ValueKind == JsonValueKind.String
                && kind.GetString() == "Modification";
        }
        catch (JsonException) { return false; }
    }

    public static int SourceRevisionNumber(string? json)
    {
        if (!IsModification(json)) return 0;
        try
        {
            using var document = JsonDocument.Parse(json!);
            return document.RootElement.TryGetProperty("modification", out var modification)
                && modification.TryGetProperty("sourceRevisionNumber", out var revision)
                && revision.TryGetInt32(out var number) && number > 0 ? number : int.MaxValue;
        }
        catch (JsonException) { return int.MaxValue; }
    }
}
