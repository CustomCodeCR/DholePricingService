using System.Data;
using System.Data.Common;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// Read/calculation surface for own LCL consolidations.
/// The stored row remains the single source of truth for raw costs; per-CBM values are derived
/// on read so they cannot become stale when the consolidation is edited.
/// </summary>
public static class OwnLclPricingMatrixEndpoints
{
    private const decimal DefaultMaximumCbm = 50m;
    private const decimal MinimumCentralAmericaProfitPerCbm = 5.70m;
    private const decimal MinimumPanamaOceanProfitPerCbm = 3.40m;

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

    private static readonly IReadOnlyDictionary<string, decimal> CentralAmericaInlandSalePerCbm =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["NI"] = 40m,
            ["HN"] = 50m,
            ["GT"] = 48m,
            ["SV"] = 40m,
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

    private const string BaseSelect = """
        SELECT id, consolidation_number, name, booking, etd, carrier_id, carrier_name, carrier_code,
               container_id, container_name, container_code, pol_id, pol_name, pol_code,
               panama_arrival_port_id, panama_arrival_port_name, panama_arrival_port_code,
               pod_id, pod_name, pod_code,
               ocean_freight, maximum_cbm, carrier_destination_cost_total, panama_to_cr_cost,
               bunker_cost, cr_transfer_base_cbm, matrix_version, status, is_active
        FROM pricing."OwnLclConsolidations"
        """;

    public static IEndpointRouteBuilder MapOwnLclPricingMatrixEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/own-lcl-consolidations")
            .WithTags("Own LCL consolidations")
            .RequireAuthorization();

        group.MapGet("/", BrowseAsync).RequireScope(PricingConstants.Scopes.RateView);
        group.MapGet("/{id:guid}", GetAsync).RequireScope(PricingConstants.Scopes.RateView);
        group.MapPost("/{id:guid}/calculate", CalculateAsync).RequireScope(PricingConstants.Scopes.RateCreate);
        return app;
    }

    private static async Task<IResult> BrowseAsync(ServiceDbContext db, CancellationToken ct)
    {
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = BaseSelect + " WHERE is_active = TRUE ORDER BY consolidation_number DESC;";

        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<OwnLclMatrixConsolidationDto>();
        while (await reader.ReadAsync(ct)) rows.Add(ReadConsolidation(reader));
        return Results.Ok(rows);
    }

    private static async Task<IResult> GetAsync(Guid id, ServiceDbContext db, CancellationToken ct)
    {
        var row = await LoadAsync(id, db, ct);
        return row is null ? Results.NotFound() : Results.Ok(row);
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

        var baseOceanCost = consolidation.OceanCostPerCbm;
        var originSurcharge = OriginSurcharges.GetValueOrDefault(requestedPol);
        var destinationCostPerCbm = consolidation.DestinationCostPerCbm;
        var panamaBaseCostPerCbm = consolidation.PanamaBaseCostPerCbm;
        var crTransferCostPerCbm = consolidation.PanamaToCostaRicaCostPerCbm;
        var costaRicaProjectedCostPerCbm = consolidation.CostaRicaProjectedCostPerCbm;

        var freightCostPerCbm = destination == "CR"
            ? costaRicaProjectedCostPerCbm + originSurcharge
            : baseOceanCost + originSurcharge;

        var historicalDestination = destination == "CR" ? "CR" : "PA";
        var historicalSale = await LoadHistoricalSaleAsync(
            consolidation.ConsolidationNumber,
            historicalDestination,
            requestedPol,
            db,
            ct);
        var minimumForRecommendation = destination == "PA"
            ? MinimumPanamaOceanProfitPerCbm
            : MinimumCentralAmericaProfitPerCbm;
        var recommendedSalePerCbm = historicalSale
            ?? CeilingCent(freightCostPerCbm + minimumForRecommendation);
        var freightSalePerCbm = request.SalePerCbm is > 0
            ? request.SalePerCbm.Value
            : recommendedSalePerCbm;

        var lines = new List<OwnLclQuoteLine>();
        AddLine(lines, "Flete Internacional Marítimo", "CBM", billableCbm, freightCostPerCbm, freightSalePerCbm);
        AddDestinationLines(lines, destination, billableCbm, destinationCostPerCbm, crTransferCostPerCbm);
        AddOriginLines(
            lines,
            incoterm,
            billableCbm,
            Math.Max(1, request.Sets),
            Math.Max(1, request.Hbl),
            request.PickupCost,
            request.PickupSale);

        var totalCost = lines.Sum(line => line.CostTotal);
        var subtotalSale = lines.Sum(line => line.SaleTotal);
        var discount = Math.Min(Math.Max(0m, request.Discount), subtotalSale);
        var finalSale = subtotalSale - discount;
        var profit = finalSale - totalCost;
        var profitPerCbm = profit / billableCbm;
        var profitPercentage = finalSale > 0 ? profit / finalSale * 100m : 0m;
        var oceanProfitPerCbm = freightSalePerCbm - freightCostPerCbm;
        var minimumProfit = destination == "PA"
            ? MinimumPanamaOceanProfitPerCbm
            : MinimumCentralAmericaProfitPerCbm;
        var meetsMinimum = destination == "PA"
            ? oceanProfitPerCbm >= MinimumPanamaOceanProfitPerCbm
            : profitPerCbm >= MinimumCentralAmericaProfitPerCbm;

        return Results.Ok(new OwnLclMatrixQuoteCalculationDto(
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
            baseOceanCost,
            originSurcharge,
            destinationCostPerCbm,
            panamaBaseCostPerCbm,
            crTransferCostPerCbm,
            costaRicaProjectedCostPerCbm,
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
        decimal destinationCostPerCbm,
        decimal crTransferCostPerCbm)
    {
        if (destination == "PA")
        {
            AddLine(lines, "Destination Charge", "CBM", cbm, destinationCostPerCbm, 20m);
            AddLine(lines, "DMCE", "HBL", 1, 65m, 65m);
            AddLine(lines, "Manejos", "HBL", 1, 25m, 25m);
            AddLine(lines, "Zone Charge", "HBL", 1, 30m, 30m);
            return;
        }

        if (destination == "CR")
        {
            AddLine(lines, "Manejos", "HBL", 1, 65m, 65m);
            AddLine(lines, "Zone Charge", "HBL", 1, 50m, 50m);
            return;
        }

        if (!CentralAmericaInlandSalePerCbm.TryGetValue(destination, out var inlandSalePerCbm)) return;

        AddLine(lines, "Transbordo", "CBM", cbm, destinationCostPerCbm, 29m);
        AddLine(lines, "Flete Terrestre", "CBM", cbm, crTransferCostPerCbm, inlandSalePerCbm);
        AddLine(lines, "Stuffing", "CBM", cbm, 10m, 10m);
        AddLine(lines, "Documentación", "HBL", 1, 140m, 140m);
        AddLine(lines, "Manejos", "HBL", 1, 45m, 45m);
        AddLine(lines, "Manejos en Destino", "HBL", 1, 70m, 70m);
    }

    private static void AddOriginLines(
        List<OwnLclQuoteLine> lines,
        string incoterm,
        decimal cbm,
        int sets,
        int hbl,
        decimal pickupCost,
        decimal pickupSale)
    {
        if (incoterm == "FOB") return;

        AddLine(lines, "CFS", "CBM", cbm, 8m, 8m);
        AddLine(lines, "CUSTOMS", "SET", sets, 15m, 25m);
        AddLine(lines, "DOC FEE", "HBL", hbl, 15m, 65m);
        AddLine(lines, "VGM", "HBL", hbl, 0m, 25m);
        AddLine(lines, "MANIFEST", "HBL", hbl, 15m, 25m);

        if (incoterm == "EXW")
            AddLine(lines, "Recolecta", "Flat", 1, Math.Max(0m, pickupCost), Math.Max(0m, pickupSale));
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

    private static async Task<decimal?> LoadHistoricalSaleAsync(
        int consolidationNumber,
        string destination,
        string polCode,
        ServiceDbContext db,
        CancellationToken ct)
    {
        await using var connection = db.Database.GetDbConnection();
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
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = BaseSelect + " WHERE id=@id AND is_active=TRUE LIMIT 1;";
        Add(command, "id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadConsolidation(reader) : null;
    }

    private static OwnLclMatrixConsolidationDto ReadConsolidation(DbDataReader reader)
    {
        var oceanFreight = reader.GetDecimal(20);
        var maximumCbm = reader.GetDecimal(21);
        var destinationTotal = reader.GetDecimal(22);
        var panamaToCostaRica = reader.GetDecimal(23);
        var bunker = reader.GetDecimal(24);
        var crTransferBaseCbm = reader.GetDecimal(25);

        var safeMaximumCbm = Math.Max(1m, maximumCbm);
        var oceanCostPerCbm = oceanFreight / safeMaximumCbm;
        var destinationCostPerCbm = destinationTotal / safeMaximumCbm;
        var panamaBaseCostPerCbm = oceanCostPerCbm + destinationCostPerCbm;
        var panamaToCostaRicaCostPerCbm = (panamaToCostaRica + bunker) / Math.Max(1m, crTransferBaseCbm);
        var costaRicaProjectedCostPerCbm = panamaBaseCostPerCbm + panamaToCostaRicaCostPerCbm;

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
            panamaToCostaRicaCostPerCbm,
            costaRicaProjectedCostPerCbm,
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
            if (compact.Equals(alias, StringComparison.OrdinalIgnoreCase) ||
                compact.Contains(alias, StringComparison.OrdinalIgnoreCase))
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

public sealed record OwnLclMatrixConsolidationDto(
    Guid Id,
    int ConsolidationNumber,
    string Name,
    string? Booking,
    DateOnly? Etd,
    Guid? CarrierId,
    string? CarrierName,
    string? CarrierCode,
    Guid? ContainerId,
    string? ContainerName,
    string? ContainerCode,
    Guid? PolId,
    string? PolName,
    string PolCode,
    Guid? PoeId,
    string? PoeName,
    string? PoeCode,
    Guid? PodId,
    string? PodName,
    string? PodCode,
    decimal OceanFreight,
    decimal MaximumCbm,
    decimal CarrierDestinationCostTotal,
    decimal PanamaToCostaRicaCost,
    decimal BunkerCost,
    decimal CostaRicaTransferBaseCbm,
    decimal OceanCostPerCbm,
    decimal DestinationCostPerCbm,
    decimal PanamaBaseCostPerCbm,
    decimal PanamaToCostaRicaCostPerCbm,
    decimal CostaRicaProjectedCostPerCbm,
    string MatrixVersion,
    string Status,
    bool IsActive);

public sealed record OwnLclMatrixQuoteCalculationDto(
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
