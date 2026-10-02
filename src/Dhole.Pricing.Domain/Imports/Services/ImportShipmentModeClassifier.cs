using System.Text.Json;

namespace Dhole.Pricing.Domain.Imports.Services;

public enum ImportedShipmentMode
{
    Unknown = 0,
    Fcl = 1,
    LclColoader = 2,
    AirLclColoader = 3,
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
        "servicetype",
        "loadtype",
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

        // LCL/AIR canonical equipment is authoritative. Historical air/LCL rows
        // may still carry a legacy FCL placeholder (for example 40HC), so FCL
        // must be validated against the extraction evidence before returning it.
        if (explicitContainerMode is ImportedShipmentMode.LclColoader or ImportedShipmentMode.AirLclColoader)
            return explicitContainerMode;

        if (string.IsNullOrWhiteSpace(rawDataJson))
            return explicitContainerMode;

        try
        {
            using var document = JsonDocument.Parse(rawDataJson);
            var equipmentValues = new List<string>();
            var modeValues = new List<string>();
            CollectRawValues(document.RootElement, equipmentValues, modeValues);

            var rawEquipmentMode = ClassifyEquipmentValues(equipmentValues.ToArray());
            if (rawEquipmentMode is ImportedShipmentMode.LclColoader or ImportedShipmentMode.AirLclColoader)
                return rawEquipmentMode;

            var rawCanonical = CanonicalText(document.RootElement.GetRawText());

            // Air evidence takes precedence over a historical 20/40/45 container
            // placeholder because old email extractions used those placeholders.
            if (HasStrongAirEvidence(rawCanonical, modeValues))
                return ImportedShipmentMode.AirLclColoader;

            if (HasStrongLclEvidence(rawCanonical, modeValues))
                return ImportedShipmentMode.LclColoader;

            if (explicitContainerMode == ImportedShipmentMode.Fcl)
                return ImportedShipmentMode.Fcl;

            if (rawEquipmentMode == ImportedShipmentMode.Fcl)
                return ImportedShipmentMode.Fcl;

            if (modeValues.Any(value =>
                    CanonicalText(value).Contains("fcl", StringComparison.Ordinal)))
            {
                return ImportedShipmentMode.Fcl;
            }
        }
        catch (JsonException)
        {
            // Malformed legacy JSON falls back to the normalized equipment snapshot.
        }

        return explicitContainerMode;
    }

    private static ImportedShipmentMode ClassifyEquipmentValues(params string?[] values)
    {
        foreach (var value in values)
        {
            var normalized = CanonicalText(value);
            if (string.IsNullOrEmpty(normalized)) continue;

            // AIR takes precedence when legacy sources contain mixed labels such
            // as "LCL AIR" or "air consolidated". In logistics, LCL alone means
            // maritime consolidation; an explicit air marker changes the modality.
            if (IsAirMarker(normalized)) return ImportedShipmentMode.AirLclColoader;
            if (IsLclMarker(normalized)) return ImportedShipmentMode.LclColoader;
            if (IsFclEquipment(normalized)) return ImportedShipmentMode.Fcl;
        }

        return ImportedShipmentMode.Unknown;
    }

    private static bool HasStrongLclEvidence(
        string rawCanonical,
        IReadOnlyCollection<string> modeValues)
    {
        if (modeValues.Any(value => IsLclMarker(CanonicalText(value))))
            return true;

        return rawCanonical.Contains("unitwm", StringComparison.Ordinal)
            || rawCanonical.Contains("lessthancontainerload", StringComparison.Ordinal)
            || rawCanonical.Contains("groupage", StringComparison.Ordinal)
            || rawCanonical.Contains("coloader", StringComparison.Ordinal)
            || rawCanonical.Contains("coloading", StringComparison.Ordinal);
    }

    private static bool HasStrongAirEvidence(
        string rawCanonical,
        IReadOnlyCollection<string> modeValues)
    {
        if (modeValues.Any(value => IsAirMarker(CanonicalText(value))))
            return true;

        if (rawCanonical.Contains("airlineroute", StringComparison.Ordinal)
            || rawCanonical.Contains("kgpercbm", StringComparison.Ordinal)
            || rawCanonical.Contains("ratebasiskgvol", StringComparison.Ordinal)
            || rawCanonical.Contains("tarifarioairdivision", StringComparison.Ordinal)
            || rawCanonical.Contains("tarifarioaereo", StringComparison.Ordinal)
            || rawCanonical.Contains("airfreight", StringComparison.Ordinal)
            || rawCanonical.Contains("aereoconsolidado", StringComparison.Ordinal))
        {
            return true;
        }

        var mentionsAirline =
            rawCanonical.Contains("aerolinea", StringComparison.Ordinal)
            || rawCanonical.Contains("airline", StringComparison.Ordinal);
        var hasVolumetricAirBasis =
            rawCanonical.Contains("167kg", StringComparison.Ordinal)
            || rawCanonical.Contains("kgvol", StringComparison.Ordinal)
            || rawCanonical.Contains("volumetric", StringComparison.Ordinal);
        var hasAirBreakpointMatrix =
            rawCanonical.Contains("airrateplus100", StringComparison.Ordinal)
            || (
                rawCanonical.Contains("minimumrate", StringComparison.Ordinal)
                && rawCanonical.Contains("ratebasis", StringComparison.Ordinal)
                && rawCanonical.Contains("kgvol", StringComparison.Ordinal)
            );
        var hasAwbEvidence = rawCanonical.Contains("awbfee", StringComparison.Ordinal)
            || rawCanonical.Contains("handlingaereo", StringComparison.Ordinal);

        return (mentionsAirline && (hasVolumetricAirBasis || hasAirBreakpointMatrix))
            || (hasAwbEvidence && hasVolumetricAirBasis);
    }

    private static bool IsLclMarker(string normalized) =>
        normalized == "lcl"
        || normalized.StartsWith("lcl", StringComparison.Ordinal)
        || normalized.Contains("lessthancontainerload", StringComparison.Ordinal)
        || normalized.Contains("loosecargo", StringComparison.Ordinal)
        || normalized.Contains("groupage", StringComparison.Ordinal)
        || normalized.Contains("coloader", StringComparison.Ordinal)
        || normalized.Contains("coloading", StringComparison.Ordinal);

    private static bool IsAirMarker(string normalized) =>
        normalized == "air"
        || normalized == "aereo"
        || normalized.StartsWith("airfreight", StringComparison.Ordinal)
        || normalized.StartsWith("airshipment", StringComparison.Ordinal)
        || normalized.StartsWith("aircargo", StringComparison.Ordinal)
        || normalized.StartsWith("airconsolidated", StringComparison.Ordinal)
        || normalized.StartsWith("airbacktoback", StringComparison.Ordinal)
        || normalized.StartsWith("airlcl", StringComparison.Ordinal)
        || normalized.StartsWith("aereoconsolidado", StringComparison.Ordinal);

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
