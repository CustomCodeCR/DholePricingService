using System.Text.Json;

namespace Dhole.Pricing.Domain.Costs.Entities;

/// <summary>
/// A flat amount selected by cargo characteristics; billing quantity is handled separately
/// by ChargeBasis. MinValue/MaxValue are inclusive for weight (KG) or package count.
/// </summary>
public sealed record CostPricingRule(
    string Dimension,
    decimal? MinValue,
    decimal? MaxValue,
    Guid? ContainerTypeId,
    decimal CostAmount,
    decimal SaleAmount
);

public static class CostPricingRules
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string? Normalize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        CostPricingRule[]? rules;
        try { rules = JsonSerializer.Deserialize<CostPricingRule[]>(json, JsonOptions); }
        catch (JsonException e) { throw new InvalidOperationException("Las condiciones del costo no son JSON válido.", e); }

        if (rules is null || rules.Length is 0 or > 100)
            throw new InvalidOperationException("Debe indicar entre 1 y 100 condiciones de costo.");

        string? dimension = null;
        foreach (var rule in rules)
        {
            if (rule.CostAmount < 0 || rule.SaleAmount < 0)
                throw new InvalidOperationException("Los montos por condición no pueden ser negativos.");
            if (rule.Dimension is not ("WeightKg" or "Packages" or "ContainerType"))
                throw new InvalidOperationException("La condición de costo debe ser Peso, Bultos o Tipo de contenedor.");
            if (dimension is not null && rule.Dimension != dimension)
                throw new InvalidOperationException("Utilice una sola dimensión por rubro; combine los filtros de ruta y modalidad normalmente.");
            dimension = rule.Dimension;

            if (rule.Dimension == "ContainerType")
            {
                if (!rule.ContainerTypeId.HasValue || rule.ContainerTypeId == Guid.Empty
                    || rule.MinValue.HasValue || rule.MaxValue.HasValue)
                    throw new InvalidOperationException("Cada condición de contenedor requiere un equipo válido.");
            }
            else
            {
                if (rule.ContainerTypeId.HasValue || (!rule.MinValue.HasValue && !rule.MaxValue.HasValue))
                    throw new InvalidOperationException("El rango de peso o bultos necesita al menos un límite.");
                if (rule.MinValue is < 0 || rule.MaxValue is < 0
                    || (rule.MinValue.HasValue && rule.MaxValue.HasValue && rule.MinValue > rule.MaxValue))
                    throw new InvalidOperationException("Los límites del rango no son válidos.");
                if (rule.Dimension == "Packages"
                    && (rule.MinValue is < 1 || rule.MaxValue is < 1
                        || (rule.MinValue.HasValue && decimal.Truncate(rule.MinValue.Value) != rule.MinValue.Value)
                        || (rule.MaxValue.HasValue && decimal.Truncate(rule.MaxValue.Value) != rule.MaxValue.Value)))
                    throw new InvalidOperationException("Los bultos deben tener límites enteros positivos.");
            }
        }

        for (var i = 0; i < rules.Length; i++)
        for (var j = i + 1; j < rules.Length; j++)
        {
            var left = rules[i];
            var right = rules[j];
            var overlap = dimension == "ContainerType"
                ? left.ContainerTypeId == right.ContainerTypeId
                : (left.MinValue ?? decimal.MinValue) <= (right.MaxValue ?? decimal.MaxValue)
                  && (right.MinValue ?? decimal.MinValue) <= (left.MaxValue ?? decimal.MaxValue);
            if (overlap)
                throw new InvalidOperationException("Las condiciones del costo no pueden solaparse ni repetirse.");
        }

        return JsonSerializer.Serialize(rules, JsonOptions);
    }

    /// <returns>false if configured conditions lack a matching cargo context; callers must not apply a default amount.</returns>
    public static bool TryResolve(
        string? json, decimal? weightKg, int? packages, Guid? containerTypeId,
        decimal fallbackCost, decimal fallbackSale,
        out decimal cost, out decimal sale)
    {
        cost = fallbackCost;
        sale = fallbackSale;
        if (string.IsNullOrWhiteSpace(json)) return true;

        var rules = JsonSerializer.Deserialize<CostPricingRule[]>(json, JsonOptions) ?? [];
        var matching = rules.FirstOrDefault(rule => rule.Dimension switch
        {
            "WeightKg" => weightKg.HasValue && weightKg >= (rule.MinValue ?? decimal.MinValue)
                && weightKg <= (rule.MaxValue ?? decimal.MaxValue),
            "Packages" => packages.HasValue && packages >= (rule.MinValue ?? decimal.MinValue)
                && packages <= (rule.MaxValue ?? decimal.MaxValue),
            "ContainerType" => containerTypeId.HasValue && rule.ContainerTypeId == containerTypeId,
            _ => false
        });
        if (matching is null) return false;

        cost = matching.CostAmount;
        sale = matching.SaleAmount;
        return true;
    }
}
