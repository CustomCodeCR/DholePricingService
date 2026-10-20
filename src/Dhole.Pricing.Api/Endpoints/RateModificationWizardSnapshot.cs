using System.Globalization;
using System.Text.Json;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// Compares seller-supplied wizard fields with authoritative saved values. Never
/// compares or exposes internal cost/margin fields, and never modifies RateHeader.
/// </summary>
internal static class RateModificationWizardSnapshot
{
    private static readonly (string Key, string Label)[] Fields = [
        ("modality", "Modalidad"),
        ("shipmentMode", "Tipo de embarque"),
        ("originId", "POL / Origen"),
        ("destinationId", "POE / Destino"),
        ("podId", "POD"),
        ("equipmentId", "Tipo de equipo"),
        ("equipmentQuantity", "Cantidad de equipos"),
        ("incotermId", "Incoterm"),
        ("serviceIds", "Servicios"),
        ("loadDate", "Fecha de carga lista"),
        ("validTo", "Vigencia"),
        ("clientName", "Cliente"),
        ("cargoCondition", "Condición de carga"),
        ("dangerousCargo", "Mercancía peligrosa"),
        ("nonStackable", "Carga no estibable"),
        ("overweight", "Sobrepeso"),
        ("cargoValue", "Valor declarado de la carga"),
        ("cargoWeightKg", "Peso de la carga (kg)"),
        ("cargoPackages", "Cantidad de bultos"),
        ("cargoPallets", "Tarimas"),
        ("cargoLengthCm", "Largo (cm)"),
        ("cargoWidthCm", "Ancho (cm)"),
        ("cargoHeightCm", "Alto (cm)"),
        ("storageDays", "Días de almacenaje"),
        ("cargoDescription", "Descripción de mercancía"),
        ("pickupAddress", "Dirección de recolección"),
        ("warehouseId", "Bodega"),
    ];

    public static bool IsEligible(RateHeader rate)
        => !rate.IsDeleted
            && rate.Status is not (RateStatus.Closed or RateStatus.Cancelled)
            && (rate.SellerReceivedAtUtc.HasValue ||
                rate.Status is RateStatus.Sent or RateStatus.RequestedByClient
                    or RateStatus.AcceptedByClient or RateStatus.RejectedByClient or RateStatus.Expired);

    private static JsonElement? FirstCargo(RateHeader rate)
    {
        if (string.IsNullOrWhiteSpace(rate.CargoLinesJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(rate.CargoLinesJson);
            return document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() > 0
                ? document.RootElement[0].Clone() : null;
        }
        catch (JsonException) { return null; }
    }

    private static string CargoString(JsonElement? cargo, string key)
        => cargo.HasValue && cargo.Value.ValueKind == JsonValueKind.Object
            && cargo.Value.TryGetProperty(key, out var value)
            ? value.ToString() : "";

    private static decimal CargoDecimal(JsonElement? cargo, string key)
        => decimal.TryParse(CargoString(cargo, key), NumberStyles.Number,
            CultureInfo.InvariantCulture, out var result) ? result : 0;

    public static object PickupLocations(RateHeader rate)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(rate.PickupLocationsJson))
                return JsonSerializer.Deserialize<JsonElement>(rate.PickupLocationsJson);
        }
        catch (JsonException) { }
        return Array.Empty<object>();
    }

    private static string CargoCondition(RateHeader rate)
    {
        try
        {
            using var document = JsonDocument.Parse(rate.PickupLocationsJson ?? "[]");
            if (document.RootElement.ValueKind == JsonValueKind.Array
                && document.RootElement.GetArrayLength() > 0)
            {
                var pickup = document.RootElement[0];
                if (pickup.ValueKind == JsonValueKind.Object
                    && pickup.TryGetProperty("cargoCondition", out var condition))
                    return condition.ToString();
            }
        }
        catch (JsonException) { }
        return "";
    }

    public static Dictionary<string, object?> OriginalForm(RateHeader rate)
    {
        var cargo = FirstCargo(rate);
        var mode = rate.ShipmentMode.ToString();
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["modality"] = mode is "Fcl" or "Lcl" ? "Maritime"
                : mode is "Ftl" or "Ltl" ? "Land" : "Air",
            ["shipmentMode"] = mode.ToUpperInvariant(),
            ["originId"] = rate.PolId.ToString(),
            ["destinationId"] = rate.PoeId.ToString(),
            ["podId"] = rate.PodId?.ToString() ?? "",
            ["equipmentId"] = rate.ContainerTypeId.ToString(),
            ["equipmentQuantity"] = rate.ContainerQuantity,
            ["incotermId"] = rate.IncotermId?.ToString() ?? "",
            ["serviceIds"] = rate.RateServices.Select(x => x.ServiceId.ToString()).OrderBy(x => x).ToArray(),
            ["loadDate"] = rate.ValidFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["validTo"] = rate.ValidTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["clientName"] = rate.ClientName ?? "",
            ["cargoCondition"] = CargoCondition(rate),
            ["dangerousCargo"] = false,
            ["nonStackable"] = false,
            ["overweight"] = false,
            ["cargoValue"] = rate.CargoValue,
            ["cargoWeightKg"] = CargoDecimal(cargo, "weightKg") is > 0m
                ? CargoDecimal(cargo, "weightKg") : rate.TotalWeightKg,
            ["cargoPackages"] = CargoDecimal(cargo, "packages") is > 0m
                ? CargoDecimal(cargo, "packages") : rate.TotalPackages,
            ["cargoPallets"] = CargoDecimal(cargo, "pallets") is > 0m
                ? CargoDecimal(cargo, "pallets") : rate.TotalPallets,
            ["cargoLengthCm"] = CargoDecimal(cargo, "lengthCm"),
            ["cargoWidthCm"] = CargoDecimal(cargo, "widthCm"),
            ["cargoHeightCm"] = CargoDecimal(cargo, "heightCm"),
            ["storageDays"] = rate.StorageDays ?? 0,
            ["cargoDescription"] = CargoString(cargo, "description"),
            ["pickupAddress"] = rate.PickupAddress ?? "",
            ["warehouseId"] = rate.WarehouseId?.ToString() ?? "",
        };
    }

    private static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }
        return null;
    }

    private static decimal Number(JsonElement element, string name)
    {
        var value = Property(element, name);
        if (!value.HasValue) return 0m;
        return decimal.TryParse(value.Value.ToString(), NumberStyles.Number,
            CultureInfo.InvariantCulture, out var result) ? result : 0m;
    }

    private static bool HasCargoLines(JsonElement? context)
    {
        if (context is not { ValueKind: JsonValueKind.Object } objectContext) return false;
        var rows = Property(objectContext, "consolidatedCargoLines");
        return rows is { ValueKind: JsonValueKind.Array } array
            && array.GetArrayLength() > 0
            && array.EnumerateArray().Any(row =>
                Number(row, "length") > 0m || Number(row, "width") > 0m
                || Number(row, "height") > 0m || Number(row, "weightKg") > 0m);
    }

    // Historic requests may only contain the top-level seller form. Recover the
    // quote's persisted cargo lines rather than displaying a zero-filled editor.
    // Dimensions in RateHeader.CargoLinesJson are always normalized to centimeters.
    public static JsonElement? SellerCargoContext(RateHeader rate, JsonElement? requestContext)
    {
        if (HasCargoLines(requestContext)) return requestContext!.Value.Clone();
        if (string.IsNullOrWhiteSpace(rate.CargoLinesJson)) return requestContext;
        try
        {
            using var document = JsonDocument.Parse(rate.CargoLinesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return requestContext;

            var rows = document.RootElement.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Object)
                .Select((row, index) => new {
                    key = "saved-quote-" + index,
                    description = Property(row, "description")?.ToString() ?? "",
                    quantity = Math.Max(1, (int)Math.Min(int.MaxValue, Math.Truncate(
                        Number(row, "packages") > 0m ? Number(row, "packages") :
                        Number(row, "pallets") > 0m ? Number(row, "pallets") : 1m))),
                    weightKg = Number(row, "weightKg"),
                    length = Number(row, "lengthCm"),
                    width = Number(row, "widthCm"),
                    height = Number(row, "heightCm"),
                    isStackable = !bool.TryParse(Property(row, "isStackable")?.ToString(), out var stackable)
                        || stackable
                })
                .Where(row => row.length > 0m || row.width > 0m || row.height > 0m
                    || row.weightKg > 0m || !string.IsNullOrWhiteSpace(row.description))
                .ToArray();
            if (rows.Length == 0) return requestContext;
            return JsonSerializer.SerializeToElement(new {
                consolidatedCargoLines = rows,
                cargoDimensionStorageUnit = "cm",
                cargoVolumeUnit = "CBM"
            });
        }
        catch (JsonException) { return requestContext; }
    }

    private static string CargoLineSummary(JsonElement row, string? dimensionsUnit)
    {
        var units = Number(row, "quantity");
        var weight = Number(row, "weightKg");
        var length = Number(row, "length");
        var width = Number(row, "width");
        var height = Number(row, "height");
        // Both sides are compared in centimeters even when the seller used inches.
        if (string.Equals(dimensionsUnit, "in", StringComparison.OrdinalIgnoreCase))
        {
            length *= 2.54m;
            width *= 2.54m;
            height *= 2.54m;
        }
        var description = Property(row, "description")?.ToString()?.Trim() ?? "";
        var isStackable = Property(row, "isStackable")?.ToString() ?? "true";
        return $"{description} · {units:0.##} unidades · {weight:0.##} kg · " +
            $"{length:0.##} × {width:0.##} × {height:0.##} cm · " +
            $"Estibable: {isStackable}";
    }

    private static List<string> CargoLines(JsonElement? context)
    {
        if (context is not { ValueKind: JsonValueKind.Object } objectContext) return [];
        var rows = Property(objectContext, "consolidatedCargoLines");
        if (rows is not { ValueKind: JsonValueKind.Array } cargoRows) return [];
        var unit = Property(objectContext, "cargoDimensionStorageUnit")?.ToString();
        return cargoRows.EnumerateArray()
            .Where(row => row.ValueKind == JsonValueKind.Object)
            .Select(row => CargoLineSummary(row, unit)).ToList();
    }

    public static object Names(RateHeader rate) => new {
        originName = rate.PolName,
        destinationName = rate.PoeName,
        podName = rate.PodName,
        equipmentName = rate.ContainerTypeName,
        incotermName = rate.IncotermName,
        serviceNames = rate.RateServices.Select(x => x.ServiceName).ToArray(),
    };

    private static string Comparable(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return "";
        if (value.ValueKind == JsonValueKind.Array)
            return string.Join("|", value.EnumerateArray().Select(Comparable).OrderBy(x => x));
        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out var numeric))
            return numeric.ToString("0.################", CultureInfo.InvariantCulture);
        if (value.ValueKind == JsonValueKind.String) return value.GetString()?.Trim() ?? "";
        return value.ToString().Trim();
    }

    private static string Friendly(string key, JsonElement value, JsonElement? names = null)
    {
        var raw = Comparable(value);
        if (string.IsNullOrEmpty(raw)) return "—";
        if (names.HasValue && names.Value.ValueKind == JsonValueKind.Object)
        {
            var labelKey = key switch {
                "originId" => "originName", "destinationId" => "destinationName",
                "podId" => "podName", "equipmentId" => "equipmentName",
                "incotermId" => "incotermName", "serviceIds" => "serviceNames", _ => ""
            };
            if (labelKey.Length > 0 && names.Value.TryGetProperty(labelKey, out var label))
            {
                var labelText = Comparable(label);
                if (!string.IsNullOrEmpty(labelText)) return labelText;
            }
        }
        return raw;
    }

    public sealed record Change(string Field, string Action, string Original, string Requested);

    public static List<Change> Compare(RateHeader rate, JsonElement payload,
        JsonElement? originalSellerContext = null)
    {
        var original = JsonSerializer.SerializeToElement(OriginalForm(rate));
        var current = payload.GetProperty("form");
        JsonElement? display = payload.TryGetProperty("requestContext", out var labels)
            && labels.ValueKind == JsonValueKind.Object ? labels : null;
        var oldNames = JsonSerializer.SerializeToElement(Names(rate));
        var changes = new List<Change>();
        foreach (var (key, label) in Fields)
        {
            if (!current.TryGetProperty(key, out var requested)) continue;
            var originalValue = original.GetProperty(key);
            if (Comparable(originalValue) == Comparable(requested)) continue;
            var wasEmpty = string.IsNullOrWhiteSpace(Comparable(originalValue));
            var isEmpty = string.IsNullOrWhiteSpace(Comparable(requested));
            changes.Add(new Change(label, wasEmpty ? "Agregar" : isEmpty ? "Eliminar" : "Modificar",
                Friendly(key, originalValue, oldNames), Friendly(key, requested, display)));
        }
        if (payload.TryGetProperty("sellerContext", out var requestedSellerContext)
            && requestedSellerContext.ValueKind == JsonValueKind.Object)
        {
            var sourceLines = CargoLines(SellerCargoContext(rate, originalSellerContext));
            var requestedLines = CargoLines(requestedSellerContext);
            for (var index = 0; index < Math.Max(sourceLines.Count, requestedLines.Count); index++)
            {
                var previous = index < sourceLines.Count ? sourceLines[index] : null;
                var proposed = index < requestedLines.Count ? requestedLines[index] : null;
                if (previous == proposed) continue;
                changes.Add(new Change($"Carga / paquete {index + 1}",
                    previous is null ? "Agregar" : proposed is null ? "Eliminar" : "Modificar",
                    previous ?? "—", proposed ?? "—"));
            }
        }
        if (payload.TryGetProperty("pickupLocations", out var pickups)
            && JsonSerializer.Serialize(PickupLocations(rate)) != JsonSerializer.Serialize(pickups))
        {
            changes.Add(new Change("Recolecciones EXW", "Modificar",
                JsonSerializer.Serialize(PickupLocations(rate)), pickups.ToString()));
        }
        if (payload.TryGetProperty("sellerContext", out var sellerContext)
            && sellerContext.ValueKind == JsonValueKind.Object
            && sellerContext.TryGetProperty("portHandlingMode", out var handling)
            && !string.IsNullOrWhiteSpace(Comparable(handling)))
        {
            changes.Add(new Change("Muellaje en destino", "Modificar",
                "Por confirmar con Pricing", Comparable(handling)));
        }
        if (payload.TryGetProperty("supportDocuments", out var docs)
            && docs.ValueKind == JsonValueKind.Array && docs.GetArrayLength() > 0)
        {
            changes.Add(new Change("Documentos de respaldo", "Agregar",
                "—", string.Join(", ", docs.EnumerateArray().Select(x =>
                    x.TryGetProperty("fileName", out var name) ? name.ToString() : "Archivo"))));
        }
        return changes;
    }
}
