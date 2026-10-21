using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dhole.Pricing.Api.Services;

public sealed record DynamicsCrudOption(string Label, JsonElement Value);
public sealed record DynamicsCrudField(string Name, string Target, string Type,
    string DataverseType, bool Required, IReadOnlyList<DynamicsCrudOption>? Options);
public sealed record DynamicsCrudSchema(string Name, int Version, string EntityName,
    string Operation, string ConnectionCode, IReadOnlyList<DynamicsCrudField> Fields);

/// <summary>
/// Translates an immutable Pricing acceptance snapshot into the active Dhole CRUD
/// contract. Choice values always come from the API schema, not list positions.
/// Unknown mandatory fields/equipment fail closed instead of creating a wrong Quote.
/// The standard totalamount is calculated by Dataverse from quotedetail, not
/// from msdyn_invoicesetuptotals, which may be absent from an active template.
/// </summary>
public static class DynamicsQuotePayloadBuilder
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static DynamicsCrudSchema ReadSchema(string response) =>
        JsonSerializer.Deserialize<DynamicsCrudSchema>(response, Json)
        ?? throw new InvalidOperationException("Empty Dynamics CRUD schema.");

    public static object Build(string acceptedSnapshot, DynamicsCrudSchema schema)
    {
        if (schema.Name != "crear_oferta" || schema.EntityName != "quote" ||
            schema.Operation != "create" || schema.ConnectionCode != "principal" ||
            schema.Fields is null)
            throw new InvalidOperationException("Unexpected Dynamics CRUD template.");
        using var document = JsonDocument.Parse(acceptedSnapshot);
        var rate = document.RootElement;
        if (String(rate, "status") != "AcceptedByClient" ||
            string.IsNullOrWhiteSpace(String(rate, "idtraNumber")))
            throw new InvalidOperationException("Only client-accepted rates with IDTRA may sync.");

        var cost = Decimal(rate, "totalCostAmount");
        var sale = Decimal(rate, "totalSaleAmount");
        if (cost < 0 || sale < 0) throw new InvalidOperationException("Invalid rate totals.");
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        var fields = schema.Fields.ToDictionary(x => x.Target, StringComparer.Ordinal);
        foreach (var required in new[] { "name", "customeridtype", "new_equipo", "new_flete",
                     "new_monto9",
                     "new_categoriatarifa", "new_preestado", "new_proyecctoespecifico",
                     "new_tipodecliente", "new_idtraasignado" })
            if (!fields.ContainsKey(required))
                throw new InvalidOperationException("Published CRUD template is missing " + required);

        foreach (var field in schema.Fields)
        {
            object? value = field.Target switch
            {
                "name" => String(rate, "rateName") ?? String(rate, "rateCode"),
                "description" => Description(rate),
                "effectivefrom" => Date(rate, "validFrom"),
                "effectiveto" => Date(rate, "validTo"),
                "msdyn_invoicesetuptotals" => sale,
                "new_flete" or "new_monto9" => cost,
                "new_equipo" => Equipment(rate, field),
                "new_cantidadequipos" => Quantity(rate),
                // Exact choices configured by the business for crear_oferta.
                "customeridtype" => "quote",
                "new_categoriatarifa" => 100000001,
                "new_preestado" => 100000003,
                "new_proyecctoespecifico" => 100000004,
                "new_tipodecliente" => 100000000,
                "new_idtraasignado" => String(rate, "idtraNumber"),
                "new_pol" => String(rate, "polName"),
                "new_poe" => String(rate, "poeName"),
                "new_pod" => String(rate, "podName"),
                "new_noincluye" => String(rate, "excludes"),
                "new_tarifaincluye" => String(rate, "includes"),
                "new_tarifasujetaa" => String(rate, "subjectTo"),
                "new_tiempodetransitoaprox" => String(rate, "transitTime"),
                _ => null
            };
            if (value is string str && string.IsNullOrWhiteSpace(str)) value = null;
            if (value is null)
            {
                if (field.Required)
                    throw new InvalidOperationException("Missing mandatory Dynamics input " + field.Name);
                continue;
            }
            if (field.DataverseType is "Picklist" or "Status" or "State")
            {
                if (value is not int integer || field.Options is null ||
                    !field.Options.Any(o => o.Value.ValueKind == JsonValueKind.Number &&
                        o.Value.TryGetInt32(out var number) && number == integer))
                    throw new InvalidOperationException("Invalid Dynamics choice for " + field.Target);
            }
            values[field.Name] = value;
        }
        return new { connectionCode = "principal", inputs = values };
    }

    private static string? String(JsonElement rate, string name) =>
        rate.ValueKind == JsonValueKind.Object &&
        rate.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString() : null;

    private static decimal Decimal(JsonElement rate, string name) =>
        rate.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Number &&
        item.TryGetDecimal(out var value)
            ? value : throw new InvalidOperationException("Rate snapshot lacks " + name);

    private static string? Date(JsonElement rate, string name)
    {
        var value = String(rate, name);
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            throw new InvalidOperationException("Invalid Pricing validity date.");
        return (parsed.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : parsed.ToUniversalTime()).ToString("O", CultureInfo.InvariantCulture);
    }

    private static string Description(JsonElement rate) =>
        string.Join(" | ", new[] { String(rate, "rateCode"), String(rate, "clientName"),
            String(rate, "shipmentMode"), String(rate, "currencyCode"),
            String(rate, "idtraNumber") }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static string Quantity(JsonElement rate)
    {
        if (rate.TryGetProperty("containers", out var containers) &&
            containers.ValueKind == JsonValueKind.Array && containers.GetArrayLength() != 0)
            return string.Join(", ", containers.EnumerateArray().Select(item =>
            {
                var count = item.TryGetProperty("quantity", out var n) && n.TryGetInt32(out var q) ? q : 1;
                return count.ToString(CultureInfo.InvariantCulture) + " x " +
                    (String(item, "containerTypeName") ?? String(item, "containerTypeCode") ?? "Equipo");
            }));
        var qty = rate.TryGetProperty("containerQuantity", out var countValue) &&
            countValue.TryGetInt32(out var amount) ? amount : 1;
        return qty.ToString(CultureInfo.InvariantCulture) + " x " +
            (String(rate, "containerTypeName") ?? String(rate, "shipmentMode") ?? "Equipo");
    }

    private static int Equipment(JsonElement rate, DynamicsCrudField field)
    {
        var mode = String(rate, "shipmentMode") ?? "";
        var types = new List<string>();
        if (rate.TryGetProperty("containers", out var containers) &&
            containers.ValueKind == JsonValueKind.Array)
            types.AddRange(containers.EnumerateArray()
                .Select(c => String(c, "containerTypeName") ?? String(c, "containerTypeCode"))
                .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!));
        if (types.Count == 0)
            types.Add(String(rate, "containerTypeName") ?? String(rate, "containerTypeCode") ?? "");
        var source = mode switch
        {
            "Lcl" => "LCL",
            "Ltl" => "LTL",
            "Air" or "AirConsol" => "AIR",
            _ => types.Count == 1 ? types[0] + " " + mode : string.Join(" - ", types) + " " + mode
        };
        var normalized = Normalize(source);
        var sourceShapes = Shapes(source);
        var matches = (field.Options ?? []).Where(option =>
        {
            var target = Normalize(option.Label);
            if (target == normalized) return true;
            if (mode is "Lcl" or "Ltl" or "Air" or "AirConsol")
                return target == Normalize(mode is "AirConsol" ? "AIR" : mode);
            if (sourceShapes.Count == 0) return false;
            var targetShapes = Shapes(option.Label);
            return sourceShapes.SequenceEqual(targetShapes, StringComparer.Ordinal) &&
                (mode != "Fcl" || !target.Contains("FTL", StringComparison.Ordinal));
        }).ToArray();
        if (matches.Length != 1 || matches[0].Value.ValueKind != JsonValueKind.Number ||
            !matches[0].Value.TryGetInt32(out var chosen))
            throw new InvalidOperationException("No unique Dynamics equipment choice for " + source);
        return chosen;
    }

    private static string Normalize(string input)
    {
        var decomposed = input.Normalize(NormalizationForm.FormD);
        var chars = decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(char.ToUpperInvariant).Where(char.IsLetterOrDigit).ToArray();
        return new string(chars)
            .Replace("HIGHCUBE", "HC", StringComparison.Ordinal)
            .Replace("FLATRACKS", "FLAT", StringComparison.Ordinal)
            .Replace("FLATRACK", "FLAT", StringComparison.Ordinal)
            .Replace("OPENTOP", "OPEN", StringComparison.Ordinal)
            .Replace("REEFER", "REEF", StringComparison.Ordinal)
            .Replace("STANDARD", "STD", StringComparison.Ordinal)
            .Replace("DRYVAN", "STD", StringComparison.Ordinal)
            .Replace("DV", "STD", StringComparison.Ordinal)
            .Replace("GP", "STD", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> Shapes(string input)
    {
        var normalized = Normalize(input);
        // One match per distinct equipment: never equate a combined type with
        // only one of its constituents.
        var matches = Regex.Matches(normalized, @"(?:20|40|45|48|53)(?:STD|HC|NOR|REEF|FLAT|OPEN|OT)?");
        return matches.Select(m => m.Value).Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();
    }
}
