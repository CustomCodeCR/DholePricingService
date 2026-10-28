using Dhole.Pricing.Domain.Rates;
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
    private const string QuoteResponseNotificationField = "new_notifrespuestacotizacion";
    private const int QuoteResponseNotificationValue = 100000000;

    public static DynamicsCrudSchema ReadSchema(string response) =>
        JsonSerializer.Deserialize<DynamicsCrudSchema>(response, Json)
        ?? throw new InvalidOperationException("Empty Dynamics CRUD schema.");

    public static object Build(string acceptedSnapshot, DynamicsCrudSchema schema,
        IConfiguration? configuration = null, Guid? resolvedCurrencyId = null,
        Guid? dholeRateId = null)
    {
        if (schema.Name != "crear_oferta" || schema.EntityName != "quote" ||
            schema.Operation is not ("create" or "upsert") || schema.ConnectionCode != "principal" ||
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
        // Reserved solely for the permanent Dhole RateId GUID. NEVER write
        // IDTRA CSV or incident references into the quote upsert identity.
        var hasRateGuidField = schema.Fields.Any(field =>
            field.Target is "cr933_dholerateid" or "new_idtraasignado");
        var guid = dholeRateId is Guid supplied && supplied != Guid.Empty
            ? supplied : Guid.TryParse(String(rate, "id"), out var extracted) && extracted != Guid.Empty
                ? extracted : Guid.Empty;
        // execute-with-total always requires this immutable identity. For
        // UPSERT, Dynamics constructs the approved alternate key from
        // dholeRateId; the IDTRA lookups are never used as that key.
        if (guid == Guid.Empty)
            throw new InvalidOperationException(
                "Dynamics synchronization requires the permanent Dhole RateId GUID.");
        // IDTRA is maintained only in the incident lookup slots. Never
        // overload the reserved GUID column with IDTRA CSV.
        if (idtraSlots.Count > 0 && Enumerable.Range(1, idtras.Length)
                .Any(index => !idtraSlots.Contains(index)))
            throw new InvalidOperationException(
                "Dynamics crear_oferta has fewer configured new_idtra1..10 lookup fields than the supplied IDTRA values.");
        // Legacy templates may rely on the immutable RateId for traceability,
        // while newer templates supply each independent IDTRA lookup. At least
        // one form of traceability must be present.
        if (idtraSlots.Count == 0 && !hasRateGuidField)
            throw new InvalidOperationException(
                "Dynamics crear_oferta requires a Dhole RateId GUID or at least one IDTRA lookup.");

        var cost = Decimal(rate, "totalCostAmount");
        var sale = Decimal(rate, "totalSaleAmount");
        if (cost < 0 || sale < 0) throw new InvalidOperationException("Invalid rate totals.");
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        var fields = schema.Fields.ToDictionary(x => x.Target, StringComparer.Ordinal);
        // Prevent creating a new QUO without the exact, authorized Dataverse
        // response-notification choice. A Boolean or similarly named field is
        // not equivalent to the approved option value 100000000.
        if (!fields.TryGetValue(QuoteResponseNotificationField, out var notification))
            throw new InvalidOperationException(
                "Published crear_oferta is missing new_notifrespuestacotizacion. Add its integer Picklist mapping and grant before syncing Quotes.");
        if (notification.Type != "integer" ||
            notification.DataverseType is not ("Picklist" or "Status" or "State") ||
            notification.Options is null || !notification.Options.Any(option =>
                option.Value.ValueKind == JsonValueKind.Number &&
                option.Value.TryGetInt32(out var choice) &&
                choice == QuoteResponseNotificationValue))
            throw new InvalidOperationException(
                "new_notifrespuestacotizacion must be an integer Dynamics choice with approved option 100000000.");
        // Both CREATE and UPSERT carry the same immutable RateId. The
        // alternate key is supplied by the connector, never by the seller.
        if (schema.Operation == "upsert" && guid == Guid.Empty)
            throw new InvalidOperationException("UPSERT requires the immutable Dhole RateId.");

        // The published CRUD template is user-configurable. The reserved
        // GUID field is optional for legacy CREATE but when present is always
        // populated with the immutable Pricing RateId.
        foreach (var required in new[] { "name", "new_equipo", "new_flete",
                     "new_categoriatarifa", "new_preestado",
                     "new_proyecctoespecifico", "new_tipodecliente" })
            if (!fields.ContainsKey(required))
                throw new InvalidOperationException("Published CRUD template is missing " + required);
        // IDTRA business references remain in the accepted Pricing snapshot,
        // and in new_idtra1..10 wherever the template exposes those lookups.

        foreach (var field in schema.Fields)
        {
            // These are optional commercial presentation texts in Dynamics.
            // Never forward them (not even an empty string/null), including
            // when a historical active template marked the input as required.
            // The accepted Pricing snapshot, tariff and PDF remain unchanged.
            if (field.Target is "new_noincluye" or "new_tarifasujetaa" or "new_tarifaincluye" ||
                PricingCustomerTarget(field.Target) ||
                field.Name is "customerid" or "customeridtype" or "msdyn_account")
                continue;
            // These quote totals are calculated and read-only in Dataverse.
            // Only their writable source fields may be included in crear_oferta.
            if (field.Target is "new_totalcostoproveedor" or "new_totalcostotarifario")
                throw new InvalidOperationException(
                    "Dynamics " + field.Target + " is calculated and read-only; configure its source fields instead.");
            var idtraIndex = IdtraLookupIndex(field);
            var montoIndex = MontoIndex(field);
            object? value = field.Target switch
            {
                // RateName is the complete immutable business title generated
                // by RateHeader.CreateRateName: QUO - Tarifa/equipment -
                // Incoterm - POL To POD Via POE - customer. Do not overwrite
                // it with the short tracking number (RateCode).
                "name" => String(rate, "rateName") ?? String(rate, "rateCode"),
                QuoteResponseNotificationField => QuoteResponseNotificationValue,
                "description" => Description(rate, idtras),
                "effectivefrom" => Date(rate, "validFrom"),
                "effectiveto" => Date(rate, "validTo"),
                "msdyn_invoicesetuptotals" => sale,
                "new_flete" => sale,
                // new_monto1 carries the consolidated supplier cost. Every
                // other amount field starts at numeric zero, not null. The
                // trusted Dynamics connector fills any fields omitted from
                // the user-managed active template before creating the QUO.
                var target when montoIndex is >= 1 and <= 9 =>
                    montoIndex == 1 ? cost : 0m,
                "new_equipo" => EquipmentForSchema(rate, field),
                "new_cantidadequipos" => Quantity(rate),
                // Exact choices configured by the business for crear_oferta.
                "new_categoriatarifa" => ConfiguredChoice(field, 100000001, configuration),
                "new_preestado" => ConfiguredChoice(field, 100000003, configuration),
                "new_proyecctoespecifico" => ConfiguredChoice(field, 100000004, configuration),
                "new_tipodecliente" => ConfiguredChoice(field, 100000000, configuration),
                "new_idtraasignado" => guid.ToString("D"),
                "cr933_dholerateid" => guid.ToString("D"),
                "new_divisa" or "new_moneda" => CurrencyForSchema(rate, field),
                var target when idtraIndex is >= 1 and <= 10
                    => idtraIndex <= idtras.Length ? idtras[idtraIndex - 1] : null,
                var target when target.EndsWith("@odata.bind", StringComparison.Ordinal) &&
                    field.Name.Equals("transactioncurrencyid", StringComparison.OrdinalIgnoreCase)
                    => CurrencyLookupValue(resolvedCurrencyId),
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
    /// Always resolve an IDTRA in Dynamics using its canonical IDTRA999.
    /// prefix. Existing immutable rate snapshots may contain a trailing
    /// -QUO reference; those remain readable without editing historical data.
    /// </summary>
    public static string[] ParseIdtraNumbers(string? raw) =>
        IdtraNumberNormalizer.Parse(raw);

    // The active tenant's USD/CRC GUID is resolved read-only through Dataverse
    // isocurrencycode. Never reuse its default currency or a hardcoded GUID.
    private static string CurrencyLookupValue(Guid? resolvedCurrencyId) =>
        resolvedCurrencyId is Guid id && id != Guid.Empty
            ? id.ToString("D")
            : throw new InvalidOperationException(
                "The quote currency was not resolved by Dataverse ISO code; no QUO was created.");

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
        string.Join(" | ", new[] { String(rate, "rateCode"),
            String(rate, "shipmentMode"), String(rate, "currencyCode"),
            string.Join(", ", idtras) }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static bool PricingCustomerTarget(string target) =>
        target.Equals("customerid", StringComparison.OrdinalIgnoreCase) ||
        target.Equals("customeridtype", StringComparison.OrdinalIgnoreCase) ||
        (target.StartsWith("customerid_", StringComparison.OrdinalIgnoreCase) &&
         target.EndsWith("@odata.bind", StringComparison.OrdinalIgnoreCase)) ||
        (target.StartsWith("msdyn_account", StringComparison.OrdinalIgnoreCase) &&
         target.EndsWith("@odata.bind", StringComparison.OrdinalIgnoreCase));

    private static string Quantity(JsonElement rate)
    {
        // LCL/LTL is not billed in full containers. A containerQuantity=0
        // must never result in a misleading "0 x LCL" on the Dynamics quote.
        if (String(rate, "shipmentMode") is "Lcl" or "Ltl" &&
            rate.TryGetProperty("chargeableQuantity", out var billable) &&
            billable.ValueKind == JsonValueKind.Number &&
            billable.TryGetDecimal(out var quantity) && quantity > 0)
            return quantity.ToString("0.####", CultureInfo.InvariantCulture) +
                (String(rate, "shipmentMode") == "Lcl" ? " CBM LCL" : " LTL");
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
