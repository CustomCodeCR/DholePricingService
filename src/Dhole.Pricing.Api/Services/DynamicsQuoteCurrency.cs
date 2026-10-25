using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Dhole.Pricing.Api.Services;

/// <summary>
/// Converts Pricing catalog currency labels into ISO 4217 before the connector's
/// read-only Dataverse lookup. A catalog code (CUR-...) is not an ISO code.
/// Never use a guessed GUID or the tenant's default transaction currency.
/// </summary>
public static class DynamicsQuoteCurrency
{
    // Previously confirmed by the operator as the USD transactioncurrencyid in
    // principal. This is only an alias/check, NEVER a substitute for the live
    // read-only ISO lookup performed by the connector before a write.
    public static readonly Guid OperatorSuppliedUsdId =
        Guid.Parse("b3a963da-2eda-e611-80f2-fc15b42826a0");

    public static string ResolveIso(string acceptedSnapshot)
    {
        using var document = System.Text.Json.JsonDocument.Parse(acceptedSnapshot);
        var root = document.RootElement;
        if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
            throw new InvalidOperationException("Invalid Pricing quote snapshot.");
        static string? Get(System.Text.Json.JsonElement root, string name) =>
            root.TryGetProperty(name, out var field) &&
            field.ValueKind == System.Text.Json.JsonValueKind.String
                ? field.GetString() : null;
        return ResolveIso(Get(root, "currencyCode"), Get(root, "currencyName"));
    }

    public static string ResolveIso(string? code, string? name)
    {
        var fromCode = Alias(code);
        var fromName = Alias(name);
        if (fromCode is not null && fromName is not null && fromCode != fromName)
            throw new InvalidOperationException(
                "Conflicting currency code and name in Pricing snapshot; no Dynamics write.");
        return fromCode ?? fromName ?? throw new InvalidOperationException(
            "Pricing currency is not a recognized USD/US DOLLAR or CRC/Costa Rican Colon value; no Dynamics write.");
    }

    public static string NormalizeSnapshot(string acceptedSnapshot, string iso)
    {
        if (iso is not ("USD" or "CRC"))
            throw new InvalidOperationException("Unsupported Dynamics ISO currency.");
        var normalized = JsonNode.Parse(acceptedSnapshot) as JsonObject
            ?? throw new InvalidOperationException("Invalid Pricing quote snapshot.");
        // Used only to build the outbound payload. The persisted acceptance
        // snapshot and its stable idempotency key are NEVER rewritten.
        normalized["currencyCode"] = iso;
        return normalized.ToJsonString();
    }

    public static void VerifyResolvedIdentity(string? sourceCode, string iso, Guid actualId)
    {
        if (actualId == Guid.Empty)
            throw new InvalidOperationException("Dynamics currency lookup returned an empty GUID.");
        // When the exact operator-supplied USD GUID appears in a legacy
        // snapshot, prevent a different currency from being silently bound.
        if (Guid.TryParse(sourceCode, out var requested) &&
            requested == OperatorSuppliedUsdId &&
            (iso != "USD" || actualId != requested))
            throw new InvalidOperationException(
                "The operator-supplied US DOLLAR GUID does not match the live Dataverse ISO lookup.");
    }

    private static string? Alias(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Guid.TryParse(value, out var id))
            return id == OperatorSuppliedUsdId ? "USD" : null;
        var plain = value.Trim().Normalize(NormalizationForm.FormD);
        var key = new string(plain.Where(c =>
                CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark &&
                char.IsLetterOrDigit(c))
            .Select(char.ToUpperInvariant).ToArray());
        return key switch
        {
            "USD" or "USDOLLAR" or "USDOLLARS" or "USDOLLARUSD" or
            "UNITEDSTATESDOLLAR" or "UNITEDSTATESDOLLARS" or
            "DOLARESTADOUNIDENSE" or "DOLARESESTADOUNIDENSES" or
            "DOLARUSA" or "DOLARAMERICANO" => "USD",
            "CRC" or "COSTARICANCOLON" or "COSTARICANCOLONES" or
            "COLONCOSTARRICENSE" or "COLONESCOSTARRICENSES" or
            "COLONCOSTARICA" => "CRC",
            _ => null
        };
    }
}
