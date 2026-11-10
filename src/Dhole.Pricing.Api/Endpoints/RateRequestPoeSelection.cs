using System.Text.Json;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// Multi-POE selections are stored in the existing seller-request JSON payload.
/// The primary PoeId/PoeName columns remain for older readers and legacy requests.
/// </summary>
public static class RateRequestPoeSelection
{
    private const int MaxPoes = 20;

    public static string? Validate(JsonElement payload, string? shipmentMode, Guid? primaryPoeId)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("selectedPoeIds", out var selected))
            return null; // Older requests do not contain an explicit POE list.

        if (selected.ValueKind != JsonValueKind.Array
            || selected.GetArrayLength() is < 1 or > MaxPoes)
            return "Seleccione entre 1 y 20 puertos POE válidos.";

        var ids = new HashSet<Guid>();
        foreach (var value in selected.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String
                || !Guid.TryParse(value.GetString(), out var id)
                || id == Guid.Empty || !ids.Add(id))
                return "Los POE deben ser identificadores válidos y no repetidos.";
        }

        if (ids.Count > 1
            && !string.Equals(shipmentMode, "Fcl", StringComparison.OrdinalIgnoreCase))
            return "La selección múltiple de POE solamente está permitida en FCL.";

        if (ids.Count > 1 && (!primaryPoeId.HasValue || !ids.Contains(primaryPoeId.Value)))
            return "El POE principal debe estar incluido en la selección de destinos.";

        return null;
    }

    public static string[] Ids(JsonElement payload, Guid? primaryPoeId)
    {
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("selectedPoeIds", out var selected)
            && selected.ValueKind == JsonValueKind.Array)
        {
            var ids = selected.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => Guid.TryParse(value.GetString(), out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .Take(MaxPoes)
                .Select(id => id.ToString("D"))
                .ToArray();
            if (ids.Length > 0) return ids;
        }

        return primaryPoeId.HasValue
            ? [primaryPoeId.Value.ToString("D")]
            : [];
    }

    public static string[] Names(JsonElement payload, string? primaryPoeName)
    {
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("selectedPoeNames", out var selected)
            && selected.ValueKind == JsonValueKind.Array)
        {
            var names = selected.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => (value.GetString() ?? "").Trim().Replace("\r", " ").Replace("\n", " "))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Length > 140 ? value[..140] : value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxPoes)
                .ToArray();
            if (names.Length > 0) return names;
        }

        return string.IsNullOrWhiteSpace(primaryPoeName)
            ? []
            : [primaryPoeName.Trim()];
    }
}
