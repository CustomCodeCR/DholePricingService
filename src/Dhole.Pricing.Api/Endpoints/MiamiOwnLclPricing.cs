using System.Globalization;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// Commercial Miami → Costa Rica LCL calculation. Its plan prices and chargeable
/// CFT rules come from cotizador_miami.html, not the China own-LCL matrix.
/// Existing quotations are immutable snapshots; this applies only on calculation.
/// </summary>
public static class MiamiOwnLclPricing
{
    private const decimal CftPerCbm = 35.3146667m;
    private const decimal KgPerCft = 14.16m;

    private sealed record Plan(
        decimal SalePerCft, decimal MinimumFreightSale,
        decimal Sed, decimal Handling, decimal Vgm, decimal Tica,
        decimal Seal, decimal Documentation, decimal Forwarding,
        decimal InsurancePercent, decimal InsuranceMinimum,
        decimal FreightCostPerCft, decimal BunkerCostPerCft, decimal ThcCostPerCft,
        decimal GriCostPerCft = 0m, bool Little = false)
    {
        public decimal TotalFreightCostPerCft =>
            FreightCostPerCft + BunkerCostPerCft + ThcCostPerCft + GriCostPerCft;
    }

    private static readonly IReadOnlyDictionary<string, Plan> Plans = new Dictionary<string, Plan>(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = new(2.80m, 280m, 25m, 45m, 20m, 25m, 20m, 25m, 0m, .75m, 50m, 1.75m, .70m, .35m),
        ["B"] = new(2.95m, 295m, 28m, 55m, 25m, 25m, 30m, 45m, 0m, .80m, 60m, 1.80m, .80m, .35m),
        ["C"] = new(3.00m, 300m, 30m, 65m, 25m, 25m, 35m, 50m, 0m, .80m, 60m, 1.85m, .80m, .35m),
        ["D"] = new(2.92m, 292m, 35m, 55m, 45m, 30m, 35m, 50m, 65m, .80m, 60m, 1.85m, .72m, .35m),
        ["NVOCC-B"] = new(2.90m, 170m, 25m, 45m, 20m, 0m, 20m, 0m, 0m, .50m, 50m, 1.85m, .70m, .35m),
        ["NVOCC-A"] = new(2.60m, 95m, 25m, 60m, 0m, 0m, 25m, 25m, 0m, .50m, 50m, 2.04m, 0m, .34m, .22m),
        ["LITTLE"] = new(0m, 0m, 0m, 35m, 15m, 0m, 0m, 20m, 0m, .50m, 10m, 0m, 0m, 0m, 0m, true),
    };

    public static bool TryCalculate(
        OwnLclConsolidationDto consolidation,
        CalculateOwnLclQuoteRequest request,
        IReadOnlyCollection<CargoCalculationLine> cargo,
        string commercialPol,
        string destination,
        string incoterm,
        out OwnLclQuoteCalculationDto? quote,
        out string? error)
    {
        quote = null;
        error = null;

        if (!string.Equals(destination, "CR", StringComparison.OrdinalIgnoreCase))
        {
            error = "El consolidado Miami únicamente está disponible para Costa Rica.";
            return false;
        }

        var planCode = string.IsNullOrWhiteSpace(request.CommercialPlan)
            ? "A" : request.CommercialPlan.Trim().ToUpperInvariant();
        if (!Plans.TryGetValue(planCode, out var plan))
        {
            error = "Seleccione una tarifa Miami válida: A, B, C, D, NVOCC-A, NVOCC-B o LITTLE.";
            return false;
        }

        var physicalVolumeCbm = cargo.Sum(x => x.DimensionalCbm);
        var totalWeightKg = cargo.Sum(x => Math.Max(0m, x.TotalWeightKg));
        var volumeCft = physicalVolumeCbm * CftPerCbm;
        var weightEquivalentCft = totalWeightKg / KgPerCft;
        // The HTML compares TOTAL volume against TOTAL weight; do not sum
        // per-package max(weight, volume). LITTLE is volume-only.
        var chargeableCft = plan.Little ? volumeCft : Math.Max(volumeCft, weightEquivalentCft);
        if (chargeableCft <= 0m)
        {
            error = "Indique dimensiones o peso válido para calcular CFT Miami.";
            return false;
        }

        if (plan.Little)
        {
            var reasons = new List<string>();
            if (totalWeightKg > 100m) reasons.Add("peso mayor a 100 kg");
            if (volumeCft > 80.01m) reasons.Add("volumen mayor a 80.01 CFT");
            if (request.CargoValue > 1000m) reasons.Add("valor de carga mayor a USD 1.000");
            if (request.IsDangerousCargo) reasons.Add("carga IMO");
            if (request.IsBonded) reasons.Add("carga Bonded");
            if (request.WhsQty > 3) reasons.Add("más de 3 WHS");
            if (reasons.Count > 0)
            {
                error = "NO APLICA PARA CARGAS LITTLE: " + string.Join(", ", reasons) + ".";
                return false;
            }
        }

        var costFreight = chargeableCft * plan.TotalFreightCostPerCft;
        var saleFreight = plan.Little
            ? volumeCft <= 30m ? 30m : volumeCft <= 60m ? 40m : 50m
            : Math.Max(chargeableCft * plan.SalePerCft, plan.MinimumFreightSale);

        var lines = new List<OwnLclQuoteLine>();
        // Flat is deliberate: a minimum must be persisted EXACTLY. The
        // originating chargeable CFT remains present in the quote summary.
        Add(lines, "Flete internacional marítimo", 1m, costFreight, saleFreight);
        if (request.IncludeSed && plan.Sed > 0m)
            Add(lines, "SED", Math.Max(1, request.SedQty), 0m, plan.Sed);
        Add(lines, "Manejos", 1m, 0m, plan.Handling);
        Add(lines, "VGM", 1m, 0m, plan.Vgm);
        Add(lines, "TICA", 1m, 0m, plan.Tica);
        Add(lines, "Marchamo", 1m, 0m, plan.Seal);
        Add(lines, "Documentación", 1m, 0m, plan.Documentation);
        Add(lines, "Forwarding", 1m, 0m, plan.Forwarding);

        if (request.CargoValue > 0m)
        {
            var insuranceSale = Math.Max(
                request.CargoValue * plan.InsurancePercent / 100m,
                plan.InsuranceMinimum);
            Add(lines, "Seguro de carga", 1m, 0m, insuranceSale);
        }

        // The reference HTML treats origin extras as separate manual values.
        // Do not add invented IMO/Bonded tariffs; preserve only the entered pickup.
        if (request.PickupCost > 0m || request.PickupSale > 0m)
            Add(lines, "Recolecta", 1m, Math.Max(0m, request.PickupCost), Math.Max(0m, request.PickupSale));

        var subtotalCost = lines.Sum(x => x.CostTotal);
        var subtotalSale = lines.Sum(x => x.SaleTotal);
        var discount = Math.Min(Math.Max(0m, request.Discount), subtotalSale);
        var finalSale = subtotalSale - discount;
        var profit = finalSale - subtotalCost;
        var billableCbm = chargeableCft / CftPerCbm;
        var freightCostPerCbm = costFreight / billableCbm;
        var freightSalePerCbm = saleFreight / billableCbm;
        var oceanProfitPerCbm = (saleFreight - costFreight) / billableCbm;
        var meetsMinimum = oceanProfitPerCbm >= 0m;

        quote = new OwnLclQuoteCalculationDto(
            consolidation.Id, consolidation.ConsolidationNumber, consolidation.Name,
            "MIA-COMMERCIAL-v2:" + planCode,
            commercialPol, destination, incoterm, cargo,
            billableCbm, billableCbm,
            freightCostPerCbm, 0m, 0m, 0m, freightCostPerCbm,
            plan.SalePerCft * CftPerCbm, freightSalePerCbm,
            lines, subtotalCost, subtotalSale, discount, finalSale,
            profit, profit / billableCbm, finalSale > 0m ? profit / finalSale * 100m : 0m,
            0m, oceanProfitPerCbm, meetsMinimum, !meetsMinimum);
        return true;
    }

    private static void Add(List<OwnLclQuoteLine> lines, string name, decimal quantity, decimal unitCost, decimal unitSale)
    {
        if (quantity <= 0m || (unitCost == 0m && unitSale == 0m)) return;
        lines.Add(new OwnLclQuoteLine(
            name, "Flat", quantity, unitCost, unitSale,
            quantity * unitCost, quantity * unitSale, quantity * (unitSale - unitCost)));
    }
}
