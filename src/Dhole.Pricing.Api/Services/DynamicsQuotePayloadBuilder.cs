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

    public static object Build(string acceptedSnapshot, DynamicsCrudSchema schema, IConfiguration? configuration = null)
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

        var idtras = ParseIdtraNumbers(String(rate, "idtraNumber"));
        if (idtras.Length == 0)
            throw new InvalidOperationException("Accepted rate has no valid IDTRA before the quote references.");
        if (idtras.Length > 10)
            throw new InvalidOperationException("A maximum of ten distinct IDTRA values can be mapped to Dynamics.");
        var idtraSlots = schema.Fields
            .Select(field => (Field: field, Index: IdtraLookupIndex(field)))
            .Where(slot => slot.Index is >= 1 and <= 10)
            .Select(slot => slot.Index)
            .ToHashSet();
        var hasIdtraText = schema.Fields.Any(field =>
            field.Target == "new_idtraasignado");
        // A text-only template can preserve the CSV; a template with lookup
        // slots must have a slot for EVERY supplied IDTRA. Never silently drop
        // a second or later relationship.
        if (idtraSlots.Count > 0 && Enumerable.Range(1, idtras.Length)
                .Any(index => !idtraSlots.Contains(index)))
            throw new InvalidOperationException(
                "Dynamics crear_oferta has fewer configured new_idtra1..10 lookup fields than the supplied IDTRA values.");
        if (idtraSlots.Count == 0 && !hasIdtraText)
            throw new InvalidOperationException(
                "Dynamics crear_oferta has no field for the normalized IDTRA values.");

        var amountSlots = schema.Fields
            .Select(MontoIndex).Where(index => index is >= 1 and <= 9).ToArray();
        var firstMappedAmount = amountSlots.Length > 0 ? amountSlots.Min() : 0;

        var cost = Decimal(rate, "totalCostAmount");
        var sale = Decimal(rate, "totalSaleAmount");
        if (cost < 0 || sale < 0) throw new InvalidOperationException("Invalid rate totals.");
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        var fields = schema.Fields.ToDictionary(x => x.Target, StringComparer.Ordinal);
        // The published CRUD template is user-configurable. customeridtype and
        // new_idtraasignado are legitimate optional fields in Dataverse and are
        // not always writable/exposed by the selected quote version. They must
        // not prevent the automatic quote + quotedetail acceptance integration.
        foreach (var required in new[] { "name", "new_equipo", "new_flete",
                     "new_categoriatarifa", "new_preestado",
                     "new_proyecctoespecifico", "new_tipodecliente" })
            if (!fields.ContainsKey(required))
                throw new InvalidOperationException("Published CRUD template is missing " + required);
        // Preserve a traceable IDTRA on at least one explicitly mapped field.
        // An existing IDTRA lookup can take the place of the optional text field.
        if (!hasIdtraText && !idtraSlots.Contains(1))
            throw new InvalidOperationException(
                "Published CRUD template must map new_idtra1 or new_idtraasignado.");

        foreach (var field in schema.Fields)
        {
            // These quote totals are calculated and read-only in Dataverse.
            // Only their writable source fields may be included in crear_oferta.
            if (field.Target is "new_totalcostoproveedor" or "new_totalcostotarifario")
                throw new InvalidOperationException(
                    "Dynamics " + field.Target + " is calculated and read-only; configure its source fields instead.");
            var idtraIndex = IdtraLookupIndex(field);
            var montoIndex = MontoIndex(field);
            object? value = field.Target switch
            {
                "name" => String(rate, "rateName") ?? String(rate, "rateCode"),
                "description" => Description(rate, idtras),
                "effectivefrom" => Date(rate, "validFrom"),
                "effectiveto" => Date(rate, "validTo"),
                "msdyn_invoicesetuptotals" => sale,
                "new_flete" => cost,
                // The quote snapshot has one consolidated supplier cost, not
                // nine independent supplier bills. Put it in the FIRST mapped
                // amount field, never in several fields (which doubles totals).
                var target when montoIndex is >= 1 and <= 9 =>
                    montoIndex == firstMappedAmount ? cost : 0m,
                "new_equipo" => EquipmentForSchema(rate, field),
                "new_cantidadequipos" => Quantity(rate),
                // Exact choices configured by the business for crear_oferta.
                "customeridtype" => "quote",
                "new_categoriatarifa" => ConfiguredChoice(field, 100000001, configuration),
                "new_preestado" => ConfiguredChoice(field, 100000003, configuration),
                "new_proyecctoespecifico" => ConfiguredChoice(field, 100000004, configuration),
                "new_tipodecliente" => ConfiguredChoice(field, 100000000, configuration),
                "new_idtraasignado" => string.Join(", ", idtras),
                "new_divisa" or "new_moneda" => CurrencyForSchema(rate, field),
                // The mapped target is a trusted navigation property in the
                // published schema, never an arbitrary caller-provided OData key.
                var target when target.EndsWith("@odata.bind", StringComparison.Ordinal) &&
                    target.Contains("customerid", StringComparison.OrdinalIgnoreCase)
                    => String(rate, "clientName"),
                // Custom account lookup selected in the current quote builder.
                // Its navigation property still comes exclusively from live metadata.
                var target when field.Name == "msdyn_account" &&
                    target.EndsWith("@odata.bind", StringComparison.Ordinal)
                    => String(rate, "clientName"),
                var target when idtraIndex is >= 1 and <= 10
                    => idtraIndex <= idtras.Length ? idtras[idtraIndex - 1] : null,
                var target when target.EndsWith("@odata.bind", StringComparison.Ordinal) &&
                    field.Name.Equals("transactioncurrencyid", StringComparison.OrdinalIgnoreCase)
                    => CurrencyLookupValue(rate, field, configuration),
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
                if (field.Options is null || !field.Options.Any(option =>
                    field.Type == "string" && value is string label &&
                        Normalize(option.Label) == Normalize(label) ||
                    field.Type == "integer" && value is int integer &&
                        option.Value.ValueKind == JsonValueKind.Number &&
                        option.Value.TryGetInt32(out var number) && number == integer))
                    throw new InvalidOperationException("Invalid Dynamics choice for " + field.Target);
            }
            values[field.Name] = value;
        }
        return new { connectionCode = "principal", inputs = values };
    }

    /// <summary>
    /// The seller's display string may include quote references after //:
    /// "IDTRA IDTRA999.12565, IDTRA999.12566 // QUO-... // QUO-...".
    /// Strip only the leading label, never the IDTRA prefix of an actual ID.
    /// </summary>
    public static string[] ParseIdtraNumbers(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        var segment = raw.Split(["//"], 2, StringSplitOptions.None)[0].Trim();
        segment = Regex.Replace(segment, @"^IDTRA(?:\s+|\s*:\s*)", "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
        return segment.Split(',', StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string CurrencyLookupValue(
        JsonElement rate, DynamicsCrudField field, IConfiguration? configuration)
    {
        var code = String(rate, "currencyCode")?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Currency is missing from the accepted Pricing snapshot.");

        // Lookup-by-name templates resolve ISO code to the actual Dataverse
        // currency ID using the approved isocurrencycode search field.
        // A GUID-mode template instead requires an explicit GUID mapping.
        if (field.Type == "string") return code;

        // Prefer an explicit ISO-currency -> Dataverse GUID mapping. This is
        // necessary because a Dataverse transactioncurrency GUID is tenant-
        // specific and MUST NOT be reused for an unrelated currency.
        var mapped = configuration?["DynamicsQuoteSync:CurrencyIds:" + code];
        // Single-currency legacy integration. The supplied tenant GUID is only
        // valid if the administrator explicitly identifies its ISO code.
        var singleCode = configuration?["DynamicsQuoteSync:SingleCurrencyCode"]?.Trim();
        if (string.IsNullOrWhiteSpace(mapped) &&
            string.Equals(singleCode, code, StringComparison.OrdinalIgnoreCase))
            mapped = configuration?["DynamicsQuoteSync:SingleCurrencyId"]
                ?? "b3a963da-2eda-e611-80f2-fc15b42826a0";

        if (Guid.TryParse(mapped, out var valid) && valid != Guid.Empty)
            return valid.ToString("D");


        throw new InvalidOperationException("No approved Dynamics transactioncurrency GUID for " +
            code + ". Configure DynamicsQuoteSync:CurrencyIds:" + code +
            " or SingleCurrencyCode/SingleCurrencyId. Do not substitute another currency.");
    }

    private static object? CurrencyForSchema(JsonElement rate, DynamicsCrudField field)
    {
        var code = String(rate, "currencyCode")?.Trim();
        if (string.IsNullOrWhiteSpace(code)) return null;
        if (field.DataverseType is not ("Picklist" or "Status" or "State"))
            return code;
        var matches = (field.Options ?? [])
            .Where(option => Normalize(option.Label) == Normalize(code) ||
                option.Value.ValueKind == JsonValueKind.String &&
                Normalize(option.Value.GetString() ?? "") == Normalize(code)).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("Dynamics currency option is absent or ambiguous for " + field.Target);
        return field.Type == "integer" && matches[0].Value.ValueKind == JsonValueKind.Number
            ? matches[0].Value.GetInt32()
            : field.Type == "string"
                ? matches[0].Label
                : throw new InvalidOperationException("Unsupported Dynamics currency choice type for " + field.Target);
    }

    private static int MontoIndex(DynamicsCrudField field)
    {
        if (field.Target.EndsWith("@odata.bind", StringComparison.Ordinal))
            return 0;
        var match = Regex.Match(field.Target, @"^new_monto([1-9])$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? int.Parse(match.Groups[1].Value,
            CultureInfo.InvariantCulture) : 0;
    }

    private static int IdtraLookupIndex(DynamicsCrudField field)
    {
        if (!field.Target.EndsWith("@odata.bind", StringComparison.Ordinal))
            return 0;
        var match = Regex.Match(field.Name, @"^new_idtra(10|[1-9])$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? int.Parse(match.Groups[1].Value,
            CultureInfo.InvariantCulture) : 0;
    }

    private static int ConfiguredChoice(DynamicsCrudField field, int preferred,
        IConfiguration? configuration)
    {
        var setting = configuration?["DynamicsQuoteSync:Choices:" + field.Target];
        if (!string.IsNullOrWhiteSpace(setting))
        {
            if (!int.TryParse(setting, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var specified))
                throw new InvalidOperationException("Invalid Dynamics choice setting for " + field.Target);
            return specified; // Verified against live Dataverse schema options in Build.
        }
        var approved = (field.Options ?? [])
            .Where(option => option.Value.ValueKind == JsonValueKind.Number &&
                             option.Value.TryGetInt32(out _))
            .Select(option => option.Value.GetInt32()).Distinct().ToArray();
        if (approved.Contains(preferred)) return preferred;
        if (approved.Length == 1) return approved[0];
        throw new InvalidOperationException("Dynamics choice for " + field.Target +
            " is ambiguous or unavailable. Configure DynamicsQuoteSync:Choices:" +
            field.Target + " using a valid option from the active schema.");
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

    private static string Description(JsonElement rate, string[] idtras) =>
        string.Join(" | ", new[] { String(rate, "rateCode"), String(rate, "clientName"),
            String(rate, "shipmentMode"), String(rate, "currencyCode"),
            string.Join(", ", idtras) }.Where(x => !string.IsNullOrWhiteSpace(x)));

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

    private static object EquipmentForSchema(JsonElement rate, DynamicsCrudField field)
    {
        var numeric = Equipment(rate, field);
        if (field.Type == "integer") return numeric;
        if (field.Type != "string")
            throw new InvalidOperationException("Unsupported equipment schema type.");
        var option = (field.Options ?? []).SingleOrDefault(o =>
            o.Value.ValueKind == JsonValueKind.Number &&
            o.Value.TryGetInt32(out var code) && code == numeric);
        return option?.Label ?? throw new InvalidOperationException("Dynamics equipment label missing.");
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
