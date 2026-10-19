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

    public static List<Change> Compare(RateHeader rate, JsonElement payload)
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
