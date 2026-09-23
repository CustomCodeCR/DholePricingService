using System.Text.Json;

namespace Dhole.Pricing.Domain.Imports.Services;

public enum ImportedShipmentMode
{
    Unknown = 0,
    Fcl = 1,
    Lcl = 2,
    Air = 3,
}

public static class ImportShipmentModeClassifier
{
    private static readonly HashSet<string> EquipmentPropertyNames = new(StringComparer.Ordinal)
    {
        "equipo",
        "equipment",
        "equipmenttype",
        "containertype",
        "containersize",
    };

    private static readonly HashSet<string> ModePropertyNames = new(StringComparer.Ordinal)
    {
        "tariffmode",
        "shipmentmode",
        "servicemode",
        "modalidad",
        "mode",
    };

    public static ImportedShipmentMode Classify(
        string? containerType,
        string? containerTypeName,
        string? containerTypeCode,
        string? containerTypeSlug,
        string? rawDataJson = null)
    {
        var explicitContainerMode = ClassifyEquipmentValues(
            containerType,
            containerTypeName,
            containerTypeCode,
            containerTypeSlug);

        // The normalized equipment snapshot is authoritative. Some historical
        // extraction payloads contain contradictory Raw.TariffMode values.
        if (explicitContainerMode != ImportedShipmentMode.Unknown)
            return explicitContainerMode;

        if (string.IsNullOrWhiteSpace(rawDataJson))
            return ImportedShipmentMode.Unknown;

        try
        {
            using var document = JsonDocument.Parse(rawDataJson);
            var equipmentValues = new List<string>();
            var modeValues = new List<string>();
            CollectRawValues(document.RootElement, equipmentValues, modeValues);

            var rawEquipmentMode = ClassifyEquipmentValues(equipmentValues.ToArray());
            if (rawEquipmentMode != ImportedShipmentMode.Unknown)
                return rawEquipmentMode;

            foreach (var value in modeValues)
            {
                var normalized = CanonicalText(value);
                if (IsLclMarker(normalized)) return ImportedShipmentMode.Lcl;
                if (IsAirMarker(normalized)) return ImportedShipmentMode.Air;
                if (normalized.Contains("fcl", StringComparison.Ordinal))
                    return ImportedShipmentMode.Fcl;
            }
        }
        catch (JsonException)
        {
            // Invalid legacy JSON remains Unknown instead of being guessed as FCL/LCL.
        }

        return ImportedShipmentMode.Unknown;
    }

    private static ImportedShipmentMode ClassifyEquipmentValues(params string?[] values)
    {
        foreach (var value in values)
        {
            var normalized = CanonicalText(value);
            if (string.IsNullOrEmpty(normalized)) continue;

            if (IsLclMarker(normalized)) return ImportedShipmentMode.Lcl;
            if (IsAirMarker(normalized)) return ImportedShipmentMode.Air;
            if (IsFclEquipment(normalized)) return ImportedShipmentMode.Fcl;
        }

        return ImportedShipmentMode.Unknown;
    }

    private static bool IsLclMarker(string normalized) =>
        normalized == "lcl"
        || normalized.Contains("lessthancontainerload", StringComparison.Ordinal)
        || normalized.Contains("loosecargo", StringComparison.Ordinal)
        || normalized.Contains("groupage", StringComparison.Ordinal);

    private static bool IsAirMarker(string normalized) =>
        normalized == "air"
        || normalized.StartsWith("aircargo", StringComparison.Ordinal)
        || normalized.StartsWith("airconsolidated", StringComparison.Ordinal)
        || normalized.StartsWith("airbacktoback", StringComparison.Ordinal);

    private static bool IsFclEquipment(string normalized)
    {
        if (normalized.Contains("fcl", StringComparison.Ordinal))
            return true;

        string[] sizes = ["20", "40", "45"];
        string[] shortCodes =
        [
            "dv", "dc", "gp", "hc", "hq", "std", "rf", "rh", "ot", "fr", "nor"
        ];
        string[] longTypes =
        [
            "dryvan", "drycontainer", "highcube", "reefer", "opentop",
            "flatrack", "standard", "nonoperatingreefer"
        ];

        foreach (var size in sizes)
        {
            if (normalized == size
                || normalized == size + "ft"
                || normalized == size + "feet")
            {
                return true;
            }

            if (shortCodes.Any(code =>
                    normalized.Contains(size + code, StringComparison.Ordinal)
                    || normalized.Contains(code + size, StringComparison.Ordinal)))
            {
                return true;
            }

            if (longTypes.Any(type =>
                    normalized.Contains(size + type, StringComparison.Ordinal)
                    || normalized.Contains(type + size, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static void CollectRawValues(
        JsonElement element,
        ICollection<string> equipmentValues,
        ICollection<string> modeValues)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var propertyName = CanonicalText(property.Name);
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        if (EquipmentPropertyNames.Contains(propertyName))
                            equipmentValues.Add(value);
                        else if (ModePropertyNames.Contains(propertyName))
                            modeValues.Add(value);
                    }
                }

                CollectRawValues(property.Value, equipmentValues, modeValues);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Array) return;

        foreach (var child in element.EnumerateArray())
            CollectRawValues(child, equipmentValues, modeValues);
    }

    private static string CanonicalText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var normalized = value
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(character =>
                System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();

        return new string(normalized);
    }
}
