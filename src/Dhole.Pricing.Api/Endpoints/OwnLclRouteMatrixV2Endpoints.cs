using System.Data;
using System.Data.Common;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// Route-aware calculation for own LCL consolidations.
/// The maritime consolidation always uses Shanghai -> Balboa as its physical base,
/// while the selected China POL and final Central-American POD are resolved from
/// the CNCA-023/#048 and CNCA-024/#049 pricing matrices.
/// </summary>
public static class OwnLclRouteMatrixV2Endpoints
{
    private const decimal MinimumCentralAmericaProfitPerCbm = 5.69m;
    private const decimal MinimumPanamaOceanProfitPerCbm = 3.40m;
    private const decimal PanamaAndCentralAmericaFreightSalePerCbm = 164m;
    private const decimal CostaRicaFreightSalePerCbm = 210m;

    // CNCA matrices: negotiated China origin differential for Costa Rica / Panama.
    private static readonly IReadOnlyDictionary<string, decimal> OriginSurcharges =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["SHANGHAI"] = 0m,
            ["NINGBO"] = 52m,
            ["QINGDAO"] = 52m,
            ["XIAMEN"] = 57m,
            ["SHANTOU"] = 57m,
            ["DALIAN"] = 57m,
            ["CHONGQING"] = 57m,
            ["FUZHOU"] = 57m,
            ["SHENZHEN"] = 62m,
            ["XINGANG"] = 62m,
            ["SHEKOU"] = 62m,
            ["GUANGZHOU"] = 62m,
        };

    private static readonly IReadOnlyDictionary<string, (string Name, decimal InlandTotal)> CentralAmericaInland =
        new Dictionary<string, (string, decimal)>(StringComparer.OrdinalIgnoreCase)
        {
            ["NI"] = ("Nicaragua / Managua", 1150m),
            ["HN"] = ("Honduras / San Pedro Sula", 1825m),
            ["SV"] = ("El Salvador / San Salvador", 2200m),
            ["GT"] = ("Guatemala", 2450m),
        };

    private static readonly IReadOnlyDictionary<string, string> ChinaPolAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SHANGHAI"] = "SHANGHAI", ["CNSHA"] = "SHANGHAI",
            ["NINGBO"] = "NINGBO", ["CNNGB"] = "NINGBO",
            ["QINGDAO"] = "QINGDAO", ["CNTAO"] = "QINGDAO", ["CNQNG"] = "QINGDAO",
            ["XIAMEN"] = "XIAMEN", ["CNXMN"] = "XIAMEN",
            ["SHANTOU"] = "SHANTOU", ["CNSWA"] = "SHANTOU",
            ["DALIAN"] = "DALIAN", ["CNDLC"] = "DALIAN",
            ["CHONGQING"] = "CHONGQING", ["CNCKG"] = "CHONGQING",
            ["FUZHOU"] = "FUZHOU", ["CNFOC"] = "FUZHOU",
            ["SHENZHEN"] = "SHENZHEN", ["CNSZX"] = "SHENZHEN",
            ["XINGANG"] = "XINGANG", ["TIANJIN"] = "XINGANG", ["CNTSN"] = "XINGANG", ["CNTXG"] = "XINGANG",
            ["SHEKOU"] = "SHEKOU", ["CNSHK"] = "SHEKOU",
            ["GUANGZHOU"] = "GUANGZHOU", ["CNCAN"] = "GUANGZHOU",
        };

    private const decimal CentralAmericaDestinationCostPerCbm = 18.825m;
    private const decimal CentralAmericaPanamaToCostaRicaTotal = 1985m;
    private const decimal CentralAmericaPanamaToCostaRicaBaseCbm = 95m;
    private const decimal CentralAmericaWarehouseTotal = 415m;
    private const decimal CentralAmericaWarehouseBaseCbm = 70m;
    private const decimal CentralAmericaInlandBaseCbm = 70m;

    private const string BaseSelect = """
        SELECT id, consolidation_number, name, booking, etd, carrier_id, carrier_name, carrier_code,
               container_id, container_name, container_code, pol_id, pol_name, pol_code,
               panama_arrival_port_id, panama_arrival_port_name, panama_arrival_port_code,
               pod_id, pod_name, pod_code,
               ocean_freight, maximum_cbm, carrier_destination_cost_total, panama_to_cr_cost,
               bunker_cost, cr_transfer_base_cbm, matrix_version, status, is_active
        FROM pricing."OwnLclConsolidations"
        """;

    public static IEndpointRouteBuilder MapOwnLclRouteMatrixV2Endpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/own-lcl-route-matrix")
            .WithTags("Own LCL route matrix")
            .RequireAuthorization();

        group.MapPost("/{id:guid}/calculate", CalculateAsync)
            .RequireScope(PricingConstants.Scopes.RateCreate);

        return app;
    }

    private static async Task<IResult> CalculateAsync(
        Guid id,
        CalculateOwnLclQuoteRequest request,
        ServiceDbContext db,
        CancellationToken ct)
    {
        var consolidation = await LoadAsync(id, db, ct);
        if (consolidation is null) return Results.NotFound();

        if (request.CargoLines.Count == 0)
            return Results.BadRequest(new { code = "Pricing.OwnLclCargoRequired", message = "Agregue al menos una línea de carga." });

        var destination = NormalizeDestination(request.DestinationCode);
        if (destination is null)
            return Results.BadRequest(new { code = "Pricing.OwnLclDestinationInvalid", message = "El destino debe ser CR, PA, NI, HN, GT o SV." });

        var incoterm = NormalizeIncoterm(request.Incoterm);
        if (incoterm is null)
            return Results.BadRequest(new { code = "Pricing.OwnLclIncotermInvalid", message = "El Incoterm debe ser FOB, FCA o EXW." });

        if (OwnLclPricingLineCatalog.IsMiamiOrigin(consolidation.PolCode, consolidation.PolName))
            return await CalculateMiamiAsync(consolidation, request, destination, incoterm, db, ct);

        var requestedPol = NormalizeChinaPol(request.PolCode);
        if (requestedPol is null && !string.IsNullOrWhiteSpace(request.PolCode))
        {
            return Results.BadRequest(new
            {
                code = "Pricing.OwnLclPolInvalid",
                message = $"No se pudo resolver el POL '{request.PolCode}'. Envíe el nombre o UN/LOCODE del puerto (por ejemplo Ningbo/CNNGB).",
            });
        }

        requestedPol ??= NormalizeChinaPol(consolidation.PolName)
            ?? NormalizeChinaPol(consolidation.PolCode)
            ?? "SHANGHAI";

        var cargo = request.CargoLines.Select(CalculateCargoLine).ToArray();
        var chargeableCbm = cargo.Sum(line => line.ChargeableCbm);
        var billableCbm = chargeableCbm > 0 ? Math.Max(1m, chargeableCbm) : 0m;
        if (billableCbm <= 0)
            return Results.BadRequest(new { code = "Pricing.OwnLclChargeableCbmRequired", message = "La carga no genera CBM cobrable." });

        var oceanCostPerCbm = consolidation.OceanFreight / Math.Max(1m, consolidation.MaximumCbm);
        var panamaDestinationCostPerCbm = consolidation.CarrierDestinationCostTotal / Math.Max(1m, consolidation.MaximumCbm);
        var panamaBaseCostPerCbm = oceanCostPerCbm + panamaDestinationCostPerCbm;

        // CNCA-023/#048 has a CR destination block of 17.265/CBM, while #049
        // and the current #050 values use 18.24/CBM. Panama remains 18.24/CBM.
        var costaRicaDestinationCostPerCbm = consolidation.ConsolidationNumber == 48
            ? 17.265m
            : panamaDestinationCostPerCbm;
        var costaRicaTransferCostPerCbm =
            (consolidation.PanamaToCostaRicaCost + consolidation.BunkerCost)
            / Math.Max(1m, consolidation.CostaRicaTransferBaseCbm);
        var costaRicaProjectedCostPerCbm = oceanCostPerCbm
            + costaRicaDestinationCostPerCbm
            + costaRicaTransferCostPerCbm;

        var isCentralAmerica = CentralAmericaInland.ContainsKey(destination);
        var configuredFreightProfitPerCbm = await LoadFreightProfitPerCbmAsync(consolidation.Id, db, ct);
        // El HTML usa exactamente el mismo diferencial de origen que Panamá
        // para Nicaragua, Honduras, Guatemala y El Salvador.
        var originSurchargePerCbm = OriginSurcharges.GetValueOrDefault(requestedPol);
        var pricingLineOverrides = await LoadPricingLineOverridesAsync(consolidation.Id, db, ct);
        if (isCentralAmerica && !pricingLineOverrides.ContainsKey("CA_TRANSSHIPMENT"))
        {
            var panamaSale = ResolveConfiguredLine(pricingLineOverrides, "PA_DESTINATION_CHARGE").Sale;
            pricingLineOverrides["CA_TRANSSHIPMENT"] = (
                panamaDestinationCostPerCbm + 9m,
                panamaSale + 9m,
                null);
        }

        decimal routeDestinationCostPerCbm;
        decimal routeTransferCostPerCbm;
        decimal routeWarehouseCostPerCbm;
        decimal routeInlandCostPerCbm;
        decimal routeCostPerCbm;

        if (destination == "PA")
        {
            routeDestinationCostPerCbm = panamaDestinationCostPerCbm;
            routeTransferCostPerCbm = 0m;
            routeWarehouseCostPerCbm = 0m;
            routeInlandCostPerCbm = 0m;
            routeCostPerCbm = oceanCostPerCbm + originSurchargePerCbm + routeDestinationCostPerCbm;
        }
        else if (destination == "CR")
        {
            routeDestinationCostPerCbm = costaRicaDestinationCostPerCbm;
            routeTransferCostPerCbm = costaRicaTransferCostPerCbm;
            routeWarehouseCostPerCbm = 0m;
            routeInlandCostPerCbm = 0m;
            routeCostPerCbm = costaRicaProjectedCostPerCbm + originSurchargePerCbm;
        }
        else
        {
            var inlandKey = CentralAmericaInlandLineKey(destination);
            var inland = ResolveConfiguredLine(pricingLineOverrides, inlandKey);
            var inlandBaseCbm = inland.CalculationBaseCbm is > 0m
                ? inland.CalculationBaseCbm.Value
                : CentralAmericaInlandBaseCbm;

            var transshipment = ResolveConfiguredLine(pricingLineOverrides, "CA_TRANSSHIPMENT");
            var stuffing = ResolveConfiguredLine(pricingLineOverrides, "CA_STUFFING");

            // Todos estos conceptos son cargos de destino separados del O/F.
            // RouteCostPerCbm es informativo; FreightCostPerCbm NO los incorpora.
            routeDestinationCostPerCbm = transshipment.Cost;
            routeTransferCostPerCbm = 0m;
            routeWarehouseCostPerCbm = stuffing.Cost;
            routeInlandCostPerCbm = inland.Cost / inlandBaseCbm;
            routeCostPerCbm = oceanCostPerCbm
                + originSurchargePerCbm
                + routeDestinationCostPerCbm
                + routeWarehouseCostPerCbm
                + routeInlandCostPerCbm;
        }

        // La utilidad/CBM configurada pertenece al consolidado y se aplica al
        // flete internacional por igual para Panamá, Costa Rica y Centroamérica.
        // La venta deja de depender de valores históricos o bases hardcodeadas.
        var freightCostPerCbm = destination == "CR"
            ? routeCostPerCbm
            : oceanCostPerCbm + originSurchargePerCbm;
        var recommendedSalePerCbm = freightCostPerCbm + configuredFreightProfitPerCbm;
        var freightSalePerCbm = recommendedSalePerCbm;

        var lines = new List<OwnLclQuoteLine>();
        AddLine(lines, "Flete Internacional Marítimo LCL", "CBM", billableCbm, freightCostPerCbm, freightSalePerCbm);
        AddDestinationLines(
            lines,
            destination,
            billableCbm,
            routeDestinationCostPerCbm,
            pricingLineOverrides);
        AddOriginLines(
            lines,
            incoterm,
            requestedPol,
            billableCbm,
            Math.Max(1, request.Sets),
            Math.Max(1, request.Hbl),
            request.PickupCost,
            request.PickupSale,
            pricingLineOverrides);

        var totalCost = lines.Sum(line => line.CostTotal);
        var subtotalSale = lines.Sum(line => line.SaleTotal);
        var discount = Math.Min(Math.Max(0m, request.Discount), subtotalSale);
        var finalSale = subtotalSale - discount;
        var profit = finalSale - totalCost;
        var profitPerCbm = profit / billableCbm;
        var profitPercentage = finalSale > 0 ? profit / finalSale * 100m : 0m;
        var oceanProfitPerCbm = freightSalePerCbm - freightCostPerCbm;
        var minimumProfit = configuredFreightProfitPerCbm;
        var meetsMinimum =
            Math.Abs(oceanProfitPerCbm - configuredFreightProfitPerCbm) <= 0.000001m;

        return Results.Ok(new OwnLclRouteMatrixQuoteDto(
            consolidation.Id,
            consolidation.ConsolidationNumber,
            consolidation.Name,
            consolidation.MatrixVersion,
            requestedPol,
            destination,
            incoterm,
            cargo,
            chargeableCbm,
            billableCbm,
            oceanCostPerCbm,
            originSurchargePerCbm,
            routeDestinationCostPerCbm,
            panamaBaseCostPerCbm,
            costaRicaTransferCostPerCbm,
            costaRicaProjectedCostPerCbm,
            routeTransferCostPerCbm,
            routeWarehouseCostPerCbm,
            routeInlandCostPerCbm,
            routeCostPerCbm,
            freightCostPerCbm,
            recommendedSalePerCbm,
            freightSalePerCbm,
            lines,
            totalCost,
            subtotalSale,
            discount,
            finalSale,
            profit,
            profitPerCbm,
            profitPercentage,
            minimumProfit,
            oceanProfitPerCbm,
            meetsMinimum,
            !meetsMinimum));
    }

    private static async Task<IResult> CalculateMiamiAsync(
        OwnLclMatrixConsolidationDto consolidation,
        CalculateOwnLclQuoteRequest request,
        string destination,
        string incoterm,
        ServiceDbContext db,
        CancellationToken ct)
    {
        var requestedPol = string.IsNullOrWhiteSpace(request.PolCode)
            ? consolidation.PolName ?? consolidation.PolCode
            : request.PolCode;
        if (!OwnLclPricingLineCatalog.IsMiamiOrigin(requestedPol, requestedPol))
        {
            return Results.BadRequest(new
            {
                code = "Pricing.OwnLclMiamiPolInvalid",
                message = $"El consolidado usa matriz Miami y no puede cotizar el POL '{request.PolCode}'.",
            });
        }

        var cargo = request.CargoLines.Select(CalculateCargoLine).ToArray();
        var chargeableCbm = cargo.Sum(line => line.ChargeableCbm);
        var billableCbm = chargeableCbm > 0 ? Math.Max(1m, chargeableCbm) : 0m;
        if (billableCbm <= 0)
            return Results.BadRequest(new { code = "Pricing.OwnLclChargeableCbmRequired", message = "La carga no genera CBM cobrable." });

        var oceanCostPerCbm = consolidation.OceanFreight / Math.Max(1m, consolidation.MaximumCbm);
        var configuredLines = await LoadPricingLineOverridesAsync(consolidation.Id, db, ct);
        var freightSalePerCbm = request.SalePerCbm is > 0
            ? request.SalePerCbm.Value
            : CeilingCent(oceanCostPerCbm + MinimumCentralAmericaProfitPerCbm);
        var recommendedSalePerCbm = CeilingCent(oceanCostPerCbm + MinimumCentralAmericaProfitPerCbm);

        var lines = new List<OwnLclQuoteLine>();
        AddLine(lines, "Flete Internacional Marítimo LCL", "CBM", billableCbm, oceanCostPerCbm, freightSalePerCbm);
        AddMiamiMatrixLines(lines, configuredLines, billableCbm);

        var matrixCost = lines.Skip(1).Sum(line => line.CostTotal);
        var matrixCostPerCbm = matrixCost / billableCbm;
        var routeCostPerCbm = oceanCostPerCbm + matrixCostPerCbm;

        var totalCost = lines.Sum(line => line.CostTotal);
        var subtotalSale = lines.Sum(line => line.SaleTotal);
        var discount = Math.Min(Math.Max(0m, request.Discount), subtotalSale);
        var finalSale = subtotalSale - discount;
        var profit = finalSale - totalCost;
        var profitPerCbm = profit / billableCbm;
        var profitPercentage = finalSale > 0 ? profit / finalSale * 100m : 0m;
        var oceanProfitPerCbm = freightSalePerCbm - oceanCostPerCbm;
        var minimumProfit = MinimumCentralAmericaProfitPerCbm;
        var meetsMinimum = profitPerCbm >= minimumProfit;

        return Results.Ok(new OwnLclRouteMatrixQuoteDto(
            consolidation.Id,
            consolidation.ConsolidationNumber,
            consolidation.Name,
            consolidation.MatrixVersion,
            "MIAMI",
            destination,
            incoterm,
            cargo,
            chargeableCbm,
            billableCbm,
            oceanCostPerCbm,
            0m,
            matrixCostPerCbm,
            routeCostPerCbm,
            0m,
            routeCostPerCbm,
            0m,
            0m,
            0m,
            routeCostPerCbm,
            oceanCostPerCbm,
            recommendedSalePerCbm,
            freightSalePerCbm,
            lines,
            totalCost,
            subtotalSale,
            discount,
            finalSale,
            profit,
            profitPerCbm,
            profitPercentage,
            minimumProfit,
            oceanProfitPerCbm,
            meetsMinimum,
            !meetsMinimum));
    }

    private static void AddMiamiMatrixLines(
        List<OwnLclQuoteLine> lines,
        IReadOnlyDictionary<string, (decimal Cost, decimal Sale, decimal? CalculationBaseCbm)> pricingLines,
        decimal cbm)
    {
        foreach (var definition in OwnLclPricingLineCatalog.Miami)
        {
            var configured = pricingLines.TryGetValue(definition.LineKey, out var stored)
                ? stored
                : (
                    Cost: definition.DefaultCostUnit ?? 0m,
                    Sale: definition.DefaultSaleUnit,
                    CalculationBaseCbm: (decimal?)null);

            var quantity = definition.ChargeBasis.Equals("CBM", StringComparison.OrdinalIgnoreCase)
                ? cbm
                : 1m;
            AddLine(
                lines,
                definition.Name,
                definition.ChargeBasis,
                quantity,
                Math.Max(0m, configured.Cost),
                Math.Max(0m, configured.Sale));
        }
    }

    private static CargoCalculationLine CalculateCargoLine(OwnLclCargoLineRequest line)
    {
        var units = Math.Max(0, line.Units);
        var dimensionalCbm = Math.Max(0m, line.LengthCm)
            * Math.Max(0m, line.WidthCm)
            * Math.Max(0m, line.HeightCm)
            * units / 1_000_000m;
        var weightCbm = Math.Max(0m, line.TotalWeightKg) / 500m;

        return new CargoCalculationLine(
            line.Description?.Trim() ?? string.Empty,
            units,
            line.TotalWeightKg,
            dimensionalCbm,
            weightCbm,
            Math.Max(dimensionalCbm, weightCbm));
    }

    private static void AddDestinationLines(
        List<OwnLclQuoteLine> lines,
        string destination,
        decimal cbm,
        decimal routeDestinationCostPerCbm,
        IReadOnlyDictionary<string, (decimal Cost, decimal Sale, decimal? CalculationBaseCbm)> pricingLines)
    {
        if (destination == "PA")
        {
            AddConfiguredLine(lines, pricingLines, "PA_DESTINATION_CHARGE", cbm, routeDestinationCostPerCbm);
            AddConfiguredLine(lines, pricingLines, "PA_DMCE", 1);
            AddConfiguredLine(lines, pricingLines, "PA_HANDLING", 1);
            AddConfiguredLine(lines, pricingLines, "PA_ZONE", 1);
            return;
        }

        if (destination == "CR")
        {
            AddConfiguredLine(lines, pricingLines, "CR_HANDLING", 1);
            AddConfiguredLine(lines, pricingLines, "CR_ZONE", 1);
            return;
        }

        // Centroamérica usa exactamente los valores guardados en el consolidado.
        // Transbordo/Stuffing/Documentación se inicializan con sus fórmulas, pero
        // permanecen editables. El flete terrestre guarda costo total + CBM base.
        AddConfiguredLine(lines, pricingLines, "CA_TRANSSHIPMENT", cbm);
        AddConfiguredCalculatedInlandLine(
            lines,
            pricingLines,
            CentralAmericaInlandLineKey(destination),
            cbm);
        AddConfiguredLine(lines, pricingLines, "CA_STUFFING", cbm);
        AddConfiguredLine(lines, pricingLines, "CA_DOCUMENTATION", 1m);
        AddConfiguredLine(lines, pricingLines, "CA_HANDLING", 1);
        AddConfiguredLine(lines, pricingLines, "CA_DESTINATION_HANDLING", 1);
    }

    private static void AddOriginLines(
        List<OwnLclQuoteLine> lines,
        string incoterm,
        string originPol,
        decimal cbm,
        int sets,
        int hbl,
        decimal pickupCost,
        decimal pickupSale,
        IReadOnlyDictionary<string, (decimal Cost, decimal Sale, decimal? CalculationBaseCbm)> pricingLines)
    {
        if (incoterm == "FOB") return;

        AddConfiguredLine(lines, pricingLines, "ORIGIN_CFS", cbm);
        // WHSE FEE applies only when the commercial POL is Shanghai. For Ningbo,
// Qingdao and the other China origins, warehouse is already included in
// the negotiated origin differential and must not be charged again.
if (string.Equals(originPol, "SHANGHAI", StringComparison.OrdinalIgnoreCase))
    AddConfiguredLine(lines, pricingLines, "ORIGIN_WHSE", cbm);
        AddConfiguredLine(lines, pricingLines, "ORIGIN_CUSTOMS", sets);
        AddConfiguredLine(lines, pricingLines, "ORIGIN_DOC", hbl);
        AddConfiguredLine(lines, pricingLines, "ORIGIN_VGM", hbl);
        AddConfiguredLine(lines, pricingLines, "ORIGIN_MANIFEST", hbl);

        if (incoterm == "EXW")
            AddConfiguredLine(lines, pricingLines, "ORIGIN_PICK_UP", 1);
    }

    private static string CentralAmericaInlandLineKey(string destination) =>
        destination switch
        {
            "NI" => "CA_INLAND_NI",
            "HN" => "CA_INLAND_HN",
            "GT" => "CA_INLAND_GT",
            "SV" => "CA_INLAND_SV",
            _ => throw new InvalidOperationException($"Destino centroamericano no soportado: {destination}."),
        };

    private static (decimal Cost, decimal Sale, decimal? CalculationBaseCbm) ResolveConfiguredLine(
        IReadOnlyDictionary<string, (decimal Cost, decimal Sale, decimal? CalculationBaseCbm)> pricingLines,
        string lineKey)
    {
        if (pricingLines.TryGetValue(lineKey, out var stored))
            return stored;

        var definition = OwnLclPricingLineCatalog.Find(lineKey)
            ?? throw new InvalidOperationException($"Línea LCL propia desconocida: {lineKey}.");
        return (
            definition.DefaultCostUnit ?? 0m,
            definition.DefaultSaleUnit,
            lineKey.StartsWith("CA_INLAND_", StringComparison.OrdinalIgnoreCase) ? 70m : null);
    }

    private static void AddConfiguredCalculatedInlandLine(
        List<OwnLclQuoteLine> lines,
        IReadOnlyDictionary<string, (decimal Cost, decimal Sale, decimal? CalculationBaseCbm)> pricingLines,
        string lineKey,
        decimal quantity)
    {
        var definition = OwnLclPricingLineCatalog.Find(lineKey)
            ?? throw new InvalidOperationException($"Línea LCL propia desconocida: {lineKey}.");
        var configured = ResolveConfiguredLine(pricingLines, lineKey);
        var baseCbm = configured.CalculationBaseCbm is > 0m ? configured.CalculationBaseCbm.Value : 70m;
        var costPerCbm = configured.Cost / baseCbm;
        AddLine(lines, definition.Name, definition.ChargeBasis, quantity, costPerCbm, configured.Sale);
    }

    private static void AddConfiguredLine(
        List<OwnLclQuoteLine> lines,
        IReadOnlyDictionary<string, (decimal Cost, decimal Sale, decimal? CalculationBaseCbm)> pricingLines,
        string lineKey,
        decimal quantity,
        decimal? fallbackCost = null)
    {
        var definition = OwnLclPricingLineCatalog.Find(lineKey)
            ?? throw new InvalidOperationException($"Línea LCL propia desconocida: {lineKey}.");
        var hasStored = pricingLines.TryGetValue(lineKey, out var stored);
        var cost = hasStored ? stored.Cost : definition.DefaultCostUnit ?? fallbackCost ?? 0m;
        if (lineKey.Equals("CR_HANDLING", StringComparison.OrdinalIgnoreCase)
            || lineKey.Equals("CR_ZONE", StringComparison.OrdinalIgnoreCase))
        {
            cost = 0m;
        }

        var sale = hasStored ? stored.Sale : definition.DefaultSaleUnit;
        AddLine(lines, definition.Name, definition.ChargeBasis, quantity, cost, sale);
    }

    private static void AddLine(
        List<OwnLclQuoteLine> lines,
        string name,
        string basis,
        decimal quantity,
        decimal costUnit,
        decimal saleUnit)
    {
        lines.Add(new OwnLclQuoteLine(
            name,
            basis,
            quantity,
            costUnit,
            saleUnit,
            quantity * costUnit,
            quantity * saleUnit,
            quantity * (saleUnit - costUnit)));
    }

    private static async Task<Dictionary<string, (decimal Cost, decimal Sale, decimal? CalculationBaseCbm)>> LoadPricingLineOverridesAsync(
        Guid consolidationId,
        ServiceDbContext db,
        CancellationToken ct)
    {
        var result = new Dictionary<string, (decimal Cost, decimal Sale, decimal? CalculationBaseCbm)>(StringComparer.OrdinalIgnoreCase);
        var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT line_key, cost_unit, sale_unit, calculation_base_cbm
            FROM pricing."OwnLclConsolidationPricingLines"
            WHERE consolidation_id=@id;
            """;
        Add(command, "id", consolidationId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result[reader.GetString(0)] = (
                reader.GetDecimal(1),
                reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetDecimal(3));
        }
        return result;
    }

    private static async Task<decimal> LoadFreightProfitPerCbmAsync(
        Guid consolidationId,
        ServiceDbContext db,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT freight_profit_per_cbm
            FROM pricing."OwnLclConsolidations"
            WHERE id=@id AND is_active=TRUE
            LIMIT 1;
            """;
        Add(command, "id", consolidationId);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull
            ? MinimumCentralAmericaProfitPerCbm
            : Math.Max(0m, Convert.ToDecimal(value));
    }

    private static async Task<decimal?> LoadHistoricalSaleAsync(
        int consolidationNumber,
        string destination,
        string polCode,
        ServiceDbContext db,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sale_per_cbm
            FROM pricing."OwnLclHistoricalRates"
            WHERE consolidation_number=@number
              AND destination_code=@destination
              AND UPPER(pol_code)=UPPER(@pol)
            LIMIT 1;
            """;
        Add(command, "number", consolidationNumber);
        Add(command, "destination", destination);
        Add(command, "pol", polCode);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : Convert.ToDecimal(value);
    }

    private static async Task<OwnLclMatrixConsolidationDto?> LoadAsync(
        Guid id,
        ServiceDbContext db,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = BaseSelect + " WHERE id=@id AND is_active=TRUE LIMIT 1;";
        Add(command, "id", id);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var oceanFreight = reader.GetDecimal(20);
        var maximumCbm = reader.GetDecimal(21);
        var destinationTotal = reader.GetDecimal(22);
        var panamaToCostaRica = reader.GetDecimal(23);
        var bunker = reader.GetDecimal(24);
        var crTransferBaseCbm = reader.GetDecimal(25);
        var oceanCostPerCbm = oceanFreight / Math.Max(1m, maximumCbm);
        var destinationCostPerCbm = destinationTotal / Math.Max(1m, maximumCbm);
        var panamaBaseCostPerCbm = oceanCostPerCbm + destinationCostPerCbm;
        var transferCostPerCbm = (panamaToCostaRica + bunker) / Math.Max(1m, crTransferBaseCbm);

        return new OwnLclMatrixConsolidationDto(
            reader.GetGuid(0),
            reader.GetInt32(1),
            reader.GetString(2),
            NullableString(reader, 3),
            NullableDate(reader, 4),
            NullableGuid(reader, 5),
            NullableString(reader, 6),
            NullableString(reader, 7),
            NullableGuid(reader, 8),
            NullableString(reader, 9),
            NullableString(reader, 10),
            NullableGuid(reader, 11),
            NullableString(reader, 12),
            reader.GetString(13),
            NullableGuid(reader, 14),
            NullableString(reader, 15),
            NullableString(reader, 16),
            NullableGuid(reader, 17),
            NullableString(reader, 18),
            NullableString(reader, 19),
            oceanFreight,
            maximumCbm,
            destinationTotal,
            panamaToCostaRica,
            bunker,
            crTransferBaseCbm,
            oceanCostPerCbm,
            destinationCostPerCbm,
            panamaBaseCostPerCbm,
            transferCostPerCbm,
            panamaBaseCostPerCbm + transferCostPerCbm,
            reader.GetString(26),
            reader.GetString(27),
            reader.GetBoolean(28));
    }

    private static string? NormalizeDestination(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Contains("COSTA RICA") || normalized.Contains("SAN JOSE") || normalized.Contains("SAN JOSÉ") ||
            normalized.Contains("GAM") || normalized.Contains("CALDERA") || normalized.Contains("LIMON") || normalized.Contains("LIMÓN") ||
            normalized.Contains("MOIN") || normalized.Contains("MOÍN") || normalized is "CR" or "SJO") return "CR";
        if (normalized.Contains("PANAMA") || normalized.Contains("PANAMÁ") || normalized.Contains("BALBOA") ||
            normalized.Contains("CFZ") || normalized.Contains("CZF") || normalized is "PA") return "PA";
        if (normalized.Contains("NICARAGUA") || normalized.Contains("MANAGUA") || normalized is "NI") return "NI";
        if (normalized.Contains("HONDURAS") || normalized.Contains("SAN PEDRO SULA") || normalized is "HN") return "HN";
        if (normalized.Contains("GUATEMALA") || normalized is "GT") return "GT";
        if (normalized.Contains("EL SALVADOR") || normalized.Contains("SAN SALVADOR") || normalized is "SV") return "SV";
        return null;
    }

    private static string? NormalizeIncoterm(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Contains("EXW")) return "EXW";
        if (normalized.Contains("FCA")) return "FCA";
        if (normalized.Contains("FOB")) return "FOB";
        return null;
    }

    private static string? NormalizeChinaPol(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0) return null;
        var compact = new string(normalized.Where(char.IsLetterOrDigit).ToArray());

        foreach (var pair in ChinaPolAliases.OrderByDescending(pair => pair.Key.Length))
        {
            var alias = new string(pair.Key.Where(char.IsLetterOrDigit).ToArray());
            if (compact.Equals(alias, StringComparison.OrdinalIgnoreCase)
                || compact.Contains(alias, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }

        return null;
    }

    private static decimal CeilingCent(decimal value) => Math.Ceiling(value * 100m) / 100m;
    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    private static string? NullableString(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static Guid? NullableGuid(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    private static DateOnly? NullableDate(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : DateOnly.FromDateTime(reader.GetDateTime(ordinal));

    private static async Task EnsureOpenAsync(DbConnection connection, CancellationToken ct)
    {
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = $"@{name}";
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}

public sealed record OwnLclRouteMatrixQuoteDto(
    Guid ConsolidationId,
    int ConsolidationNumber,
    string ConsolidationName,
    string MatrixVersion,
    string PolCode,
    string DestinationCode,
    string Incoterm,
    IReadOnlyCollection<CargoCalculationLine> CargoLines,
    decimal ChargeableCbm,
    decimal BillableCbm,
    decimal BaseOceanCostPerCbm,
    decimal OriginSurchargePerCbm,
    decimal DestinationCostPerCbm,
    decimal PanamaBaseCostPerCbm,
    decimal CostaRicaTransferCostPerCbm,
    decimal CostaRicaProjectedCostPerCbm,
    decimal RouteTransferCostPerCbm,
    decimal RouteWarehouseCostPerCbm,
    decimal RouteInlandCostPerCbm,
    decimal RouteCostPerCbm,
    decimal FreightCostPerCbm,
    decimal RecommendedSalePerCbm,
    decimal FreightSalePerCbm,
    IReadOnlyCollection<OwnLclQuoteLine> Lines,
    decimal TotalCost,
    decimal SubtotalSale,
    decimal Discount,
    decimal FinalSale,
    decimal ProfitAmount,
    decimal ProfitPerCbm,
    decimal ProfitPercentage,
    decimal MinimumProfitPerCbm,
    decimal OceanProfitPerCbm,
    bool MeetsMinimumMargin,
    bool RequiresLowMarginApproval);
