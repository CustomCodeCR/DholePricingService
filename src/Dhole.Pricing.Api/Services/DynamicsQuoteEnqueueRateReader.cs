using System.Globalization;
using System.Text.Json;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Api.Services;

/// <summary>
/// Reads a single locked RateHeaders row without asking EF Core to materialize
/// legacy non-nullable columns or convert historical enum values. The database
/// JSON object is normalized into a strictly typed, validated Dynamics snapshot.
/// No fallback to a different rate or a fabricated amount is permitted.
/// </summary>
public static class DynamicsQuoteEnqueueRateReader
{
    public static DynamicsQuoteEnqueueRate Parse(string databaseRow)
    {
        using var document = JsonDocument.Parse(databaseRow);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid RateHeaders row JSON.");

        // PostgreSQL installations may use PascalCase or snake_case column names.
        // Match names, not positions, without relying on EF's materialization rules.
        var columns = document.RootElement.EnumerateObject()
            .ToDictionary(item => Normalize(item.Name), item => item.Value,
                StringComparer.OrdinalIgnoreCase);

        JsonElement Field(string name) =>
            columns.TryGetValue(Normalize(name), out var item) ? item : default;

        string? Text(string name)
        {
            var item = Field(name);
            return item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => throw new InvalidDataException("Invalid RateHeaders field: " + name)
            };
        }

        string RequiredText(string name) =>
            !string.IsNullOrWhiteSpace(Text(name))
                ? Text(name)!
                : throw new InvalidDataException("Missing RateHeaders field: " + name);

        int Integer(string name)
        {
            var item = Field(name);
            return item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var n)
                ? n
                : throw new InvalidDataException("Invalid RateHeaders field: " + name);
        }

        decimal Amount(string name)
        {
            var item = Field(name);
            return item.ValueKind == JsonValueKind.Number && item.TryGetDecimal(out var n)
                ? n
                : throw new InvalidDataException("Invalid RateHeaders field: " + name);
        }

        DateTime Date(string name) =>
            DateTime.TryParse(Text(name), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var date)
                ? date
                : throw new InvalidDataException("Invalid RateHeaders field: " + name);

        string EnumName<T>(string name) where T : struct, Enum
        {
            var raw = RequiredText(name);
            return Enum.TryParse<T>(raw, true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed.ToString()
                : throw new InvalidDataException("Invalid RateHeaders enum: " + name);
        }

        var idField = Field("Id");
        if (idField.ValueKind != JsonValueKind.String ||
            !idField.TryGetGuid(out var id) || id == Guid.Empty)
            throw new InvalidDataException("Invalid RateHeaders field: Id");

        var deletedField = Field("IsDeleted");
        var deleted = deletedField.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False or JsonValueKind.Null or JsonValueKind.Undefined => false,
            _ => throw new InvalidDataException("Invalid RateHeaders field: IsDeleted")
        };

        return new DynamicsQuoteEnqueueRate(
            id, Integer("RevisionNumber"), deleted, EnumName<RateStatus>("Status"),
            Text("IdtraNumber"), Text("RateCode"), Text("RateName"),
            Text("ClientName"), Text("ExecutiveName"),
            Amount("TotalCostAmount"), Amount("TotalSaleAmount"),
            RequiredText("CurrencyCode"), Text("CurrencyName"),
            EnumName<ShipmentMode>("ShipmentMode"), EnumName<RateType>("RateType"),
            Text("IncotermName"), Integer("ContainerQuantity"),
            Text("ContainerTypeName"), Text("ContainerTypeCode"),
            Amount("ChargeableQuantity"), Date("ValidFrom"), Date("ValidTo"),
            Text("PolName"), Text("PoeName"), Text("PodName"),
            Text("Includes"), Text("SubjectTo"), Text("Excludes"), Text("TransitTime")
        );
    }

    private static string Normalize(string name) =>
        new(name.Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant).ToArray());
}

public sealed record DynamicsQuoteEnqueueRate(
    Guid Id, int RevisionNumber, bool IsDeleted, string Status, string? IdtraNumber,
    string? RateCode, string? RateName, string? ClientName, string? ExecutiveName,
    decimal TotalCostAmount, decimal TotalSaleAmount, string CurrencyCode,
    string? CurrencyName, string ShipmentMode, string RateType,
    string? IncotermName, int ContainerQuantity, string? ContainerTypeName,
    string? ContainerTypeCode, decimal ChargeableQuantity, DateTime ValidFrom,
    DateTime ValidTo, string? PolName, string? PoeName, string? PodName,
    string? Includes, string? SubjectTo, string? Excludes, string? TransitTime)
{
    public string ToPayloadJson(object containers) =>
        JsonSerializer.Serialize(new
        {
            Id, RateCode, RateName, RevisionNumber,
            ClientName, ExecutiveName, IdtraNumber,
            TotalCostAmount, TotalSaleAmount, CurrencyCode, CurrencyName,
            IncotermName, PolName, PoeName, PodName, ContainerQuantity,
            ContainerTypeName, ContainerTypeCode, ChargeableQuantity,
            ValidFrom, ValidTo, Includes, SubjectTo, Excludes, TransitTime,
            RateType, ShipmentMode, Status, Containers = containers
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
