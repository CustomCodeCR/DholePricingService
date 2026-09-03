using System.Data;
using System.Data.Common;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class OwnLclConsolidationEndpoints
{
    private const decimal DefaultMaximumCbm = 50m;
    private const decimal MinimumCentralAmericaProfitPerCbm = 5.70m;
    private const decimal MinimumPanamaOceanProfitPerCbm = 3.40m;
    private const decimal CentralAmericaOperationBaseCbm = 70m;
    private const decimal CostaRicaWarehouseOperation = 415m;

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

    private static readonly IReadOnlyDictionary<string, (string Label, decimal Total)> CentralAmericaLandFreight =
        new Dictionary<string, (string Label, decimal Total)>(StringComparer.OrdinalIgnoreCase)
        {
            ["NI"] = ("Flete Terrestre CRC → Nicaragua", 1150m),
            ["HN"] = ("Flete Terrestre CRC → San Pedro Sula, Honduras", 1825m),
            ["SV"] = ("Flete Terrestre CRC → El Salvador", 2200m),
            ["GT"] = ("Flete Terrestre CRC → Guatemala", 2450m),
        };

    public static IEndpointRouteBuilder MapOwnLclConsolidationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/own-lcl-consolidations")
            .WithTags("Own LCL consolidations")
            .RequireAuthorization();

        group.MapGet("/", BrowseAsync).RequireScope(PricingConstants.Scopes.RateView);
        group.MapGet("/{id:guid}", GetAsync).RequireScope(PricingConstants.Scopes.RateView);
        group.MapPost("/", CreateAsync).RequireScope(PricingConstants.Scopes.OwnLclConsolidationCreate);
        group.MapPut("/{id:guid}", UpdateAsync).RequireScope(PricingConstants.Scopes.RateUpdate);
        group.MapPost("/{id:guid}/calculate", CalculateAsync).RequireScope(PricingConstants.Scopes.RateCreate);

        return app;
    }

    private static async Task<IResult> BrowseAsync(ServiceDbContext db, CancellationToken ct)
    {
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, consolidation_number, name, booking, etd, carrier_id, carrier_name, carrier_code,
                   container_id, container_name, container_code, pol_id, pol_name, pol_code,
                   panama_arrival_port_id, panama_arrival_port_name, panama_arrival_port_code,
                   pod_id, pod_name, pod_code,
                   ocean_freight, maximum_cbm, carrier_destination_cost_total, panama_to_cr_cost,
                   bunker_cost, cr_transfer_base_cbm, matrix_version, status, is_active
            FROM pricing."OwnLclConsolidations"
            WHERE is_active = TRUE
            ORDER BY consolidation_number DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<OwnLclConsolidationDto>();
        while (await reader.ReadAsync(ct)) result.Add(ReadConsolidation(reader));
        return Results.Ok(result);
    }

    private static async Task<IResult> GetAsync(Guid id, ServiceDbContext db, CancellationToken ct)
    {
        var row = await LoadAsync(id, db, ct);
        return row is null ? Results.NotFound() : Results.Ok(row);
    }

    private static async Task<IResult> CreateAsync(CreateOwnLclConsolidationRequest request, ServiceDbContext db, CancellationToken ct)
    {
        var validation = Validate(request.Booking, request.PolCode, request.OceanFreight, request.MaximumCbm);
        if (validation is not null) return Results.BadRequest(validation);

        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        int nextNumber;
        await using (var sequence = connection.CreateCommand())
        {
            sequence.Transaction = tx;
            sequence.CommandText = "SELECT GREATEST(COALESCE(MAX(consolidation_number), 47) + 1, 48) FROM pricing.\"OwnLclConsolidations\";";
            nextNumber = Convert.ToInt32(await sequence.ExecuteScalarAsync(ct));
        }

        var id = Guid.NewGuid();
        var maxCbm = request.MaximumCbm is > 0 ? request.MaximumCbm.Value : DefaultMaximumCbm;
        var version = $"CNCA-{nextNumber:000}-v1";

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = """
                INSERT INTO pricing."OwnLclConsolidations"
                    (id, consolidation_number, name, booking, etd, carrier_id, carrier_name, carrier_code,
                     container_id, container_name, container_code, pol_id, pol_name, pol_code,
                     ocean_freight, maximum_cbm, carrier_destination_cost_total, panama_to_cr_cost,
                     bunker_cost, cr_transfer_base_cbm, matrix_version, status, is_active, created_at_utc)
                VALUES
                    (@id, @number, @name, @booking, @etd, @carrier_id, @carrier_name, @carrier_code,
                     @container_id, @container_name, @container_code, @pol_id, @pol_name, @pol_code,
                     @ocean_freight, @maximum_cbm, @destination_cost, @panama_to_cr, @bunker,
                     @cr_base, @version, 'Draft', TRUE, now());
                """;
            Add(command, "id", id);
            Add(command, "number", nextNumber);
            Add(command, "name", $"Consolidado {nextNumber}");
            Add(command, "booking", NullIfBlank(request.Booking));
            Add(command, "etd", request.Etd);
            Add(command, "carrier_id", request.CarrierId);
            Add(command, "carrier_name", NullIfBlank(request.CarrierName));
            Add(command, "carrier_code", NullIfBlank(request.CarrierCode));
            Add(command, "container_id", request.ContainerId);
            Add(command, "container_name", NullIfBlank(request.ContainerName));
            Add(command, "container_code", NullIfBlank(request.ContainerCode));
            Add(command, "pol_id", request.PolId);
            Add(command, "pol_name", NullIfBlank(request.PolName));
            Add(command, "pol_code", NormalizeCode(request.PolCode));
            Add(command, "ocean_freight", request.OceanFreight);
            Add(command, "maximum_cbm", maxCbm);
            Add(command, "destination_cost", request.CarrierDestinationCostTotal ?? 912m);
            Add(command, "panama_to_cr", request.PanamaToCostaRicaCost ?? 2140m);
            Add(command, "bunker", request.BunkerCost ?? 280m);
            Add(command, "cr_base", request.CostaRicaTransferBaseCbm is > 0 ? request.CostaRicaTransferBaseCbm.Value : 95m);
            Add(command, "version", version);
            await command.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return Results.Created($"/api/pricing/own-lcl-consolidations/{id}", new { id, consolidationNumber = nextNumber, name = $"Consolidado {nextNumber}", matrixVersion = version });
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateOwnLclConsolidationRequest request, ServiceDbContext db, CancellationToken ct)
    {
        var validation = Validate(request.Booking, request.PolCode, request.OceanFreight, request.MaximumCbm);
        if (validation is not null) return Results.BadRequest(validation);

        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE pricing."OwnLclConsolidations"
            SET booking=@booking, etd=@etd, carrier_id=@carrier_id, carrier_name=@carrier_name, carrier_code=@carrier_code,
                container_id=@container_id, container_name=@container_name, container_code=@container_code,
                pol_id=@pol_id, pol_name=@pol_name, pol_code=@pol_code, ocean_freight=@ocean_freight,
                maximum_cbm=@maximum_cbm, carrier_destination_cost_total=@destination_cost,
                panama_to_cr_cost=@panama_to_cr, bunker_cost=@bunker, cr_transfer_base_cbm=@cr_base,
                matrix_version = CASE WHEN matrix_version LIKE '%-v1' THEN replace(matrix_version, '-v1', '-v2') ELSE matrix_version END,
                updated_at_utc=now()
            WHERE id=@id AND is_active=TRUE;
            """;
        Add(command, "id", id);
        Add(command, "booking", NullIfBlank(request.Booking));
        Add(command, "etd", request.Etd);
        Add(command, "carrier_id", request.CarrierId);
        Add(command, "carrier_name", NullIfBlank(request.CarrierName));
        Add(command, "carrier_code", NullIfBlank(request.CarrierCode));
        Add(command, "container_id", request.ContainerId);
        Add(command, "container_name", NullIfBlank(request.ContainerName));
        Add(command, "container_code", NullIfBlank(request.ContainerCode));
        Add(command, "pol_id", request.PolId);
        Add(command, "pol_name", NullIfBlank(request.PolName));
        Add(command, "pol_code", NormalizeCode(request.PolCode));
        Add(command, "ocean_freight", request.OceanFreight);
        Add(command, "maximum_cbm", request.MaximumCbm > 0 ? request.MaximumCbm : DefaultMaximumCbm);
        Add(command, "destination_cost", request.CarrierDestinationCostTotal);
        Add(command, "panama_to_cr", request.PanamaToCostaRicaCost);
        Add(command, "bunker", request.BunkerCost);
        Add(command, "cr_base", request.CostaRicaTransferBaseCbm > 0 ? request.CostaRicaTransferBaseCbm : 95m);

        return await command.ExecuteNonQueryAsync(ct) == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> CalculateAsync(Guid id, CalculateOwnLclQuoteRequest request, ServiceDbContext db, CancellationToken ct)
    {
        var consolidation = await LoadAsync(id, db, ct);
        if (consolidation is null) return Results.NotFound();
        if (request.CargoLines.Count == 0) return Results.BadRequest(new { code = "Pricing.OwnLclCargoRequired", message = "Agregue al menos una línea de carga." });

        var destination = NormalizeDestination(request.DestinationCode);
        if (destination is null)
            return Results.BadRequest(new { code = "Pricing.OwnLclDestinationInvalid", message = "El destino debe ser CR, PA, NI, HN, GT o SV." });

        var incoterm = NormalizeCode(request.Incoterm);
        if (incoterm is not ("FOB" or "FCA" or "EXW"))
            return Results.BadRequest(new { code = "Pricing.OwnLclIncotermInvalid", message = "El Incoterm debe ser FOB, FCA o EXW." });

        var cargo = request.CargoLines.Select(CalculateCargoLine).ToArray();
        var chargeableCbm = cargo.Sum(x => x.ChargeableCbm);
        var billableCbm = chargeableCbm > 0 ? Math.Max(1m, chargeableCbm) : 0m;
        if (billableCbm <= 0) return Results.BadRequest(new { code = "Pricing.OwnLclChargeableCbmRequired", message = "La carga no genera CBM cobrable." });

        var maximumCbm = consolidation.MaximumCbm > 0 ? consolidation.MaximumCbm : DefaultMaximumCbm;
        var baseOceanCost = consolidation.OceanFreight / maximumCbm;
        var originSurcharge = OriginSurcharges.GetValueOrDefault(NormalizeCode(request.PolCode ?? consolidation.PolCode));
        var oceanCostWithOrigin = baseOceanCost + originSurcharge;
        var destinationCostPerCbm = consolidation.CarrierDestinationCostTotal / maximumCbm;
        var crTransferCostPerCbm = (consolidation.PanamaToCostaRicaCost + consolidation.BunkerCost) / Math.Max(1m, consolidation.CostaRicaTransferBaseCbm);

        // Panamá termina el tramo marítimo en Balboa. Costa Rica y el resto de
        // Centroamérica continúan por CFZ y el tramo terrestre Panamá -> CRC.
        var freightCostPerCbm = destination == "PA"
            ? oceanCostWithOrigin
            : oceanCostWithOrigin + destinationCostPerCbm + crTransferCostPerCbm;

        var historicalDestination = destination;
        var historicalSale = await LoadHistoricalSaleAsync(consolidation.ConsolidationNumber, historicalDestination, request.PolCode ?? consolidation.PolCode, db, ct);
        var minimumForRecommendation = destination == "PA" ? MinimumPanamaOceanProfitPerCbm : MinimumCentralAmericaProfitPerCbm;
        var calculatedRecommended = CeilingCent(freightCostPerCbm + minimumForRecommendation);
        var recommendedSalePerCbm = historicalSale ?? calculatedRecommended;
        var freightSalePerCbm = request.SalePerCbm is > 0 ? request.SalePerCbm.Value : recommendedSalePerCbm;

        var lines = new List<OwnLclQuoteLine>();
        AddLine(lines, "Flete Internacional Marítimo", "CBM", billableCbm, freightCostPerCbm, freightSalePerCbm);
        AddDestinationLines(lines, destination, billableCbm, destinationCostPerCbm);
        AddOriginLines(lines, incoterm, billableCbm, Math.Max(1, request.Sets), Math.Max(1, request.Hbl), request.PickupCost, request.PickupSale);

        var subtotalCost = lines.Sum(x => x.CostTotal);
        var subtotalSale = lines.Sum(x => x.SaleTotal);
        var discount = Math.Min(Math.Max(0m, request.Discount), subtotalSale);
        var finalSale = subtotalSale - discount;
        var profit = finalSale - subtotalCost;
        var profitPerCbm = profit / billableCbm;
        var profitPercentage = finalSale > 0 ? (profit / finalSale) * 100m : 0m;

        var oceanProfitPerCbm = freightSalePerCbm - freightCostPerCbm;
        var minimumProfit = destination == "PA" ? MinimumPanamaOceanProfitPerCbm : MinimumCentralAmericaProfitPerCbm;
        var meetsMinimum = destination == "PA"
            ? oceanProfitPerCbm >= MinimumPanamaOceanProfitPerCbm
            : profitPerCbm >= MinimumCentralAmericaProfitPerCbm;

        return Results.Ok(new OwnLclQuoteCalculationDto(
            consolidation.Id,
            consolidation.ConsolidationNumber,
            consolidation.Name,
            consolidation.MatrixVersion,
            NormalizeCode(request.PolCode ?? consolidation.PolCode),
            destination,
            incoterm,
            cargo,
            chargeableCbm,
            billableCbm,
            baseOceanCost,
            originSurcharge,
            destinationCostPerCbm,
            crTransferCostPerCbm,
            freightCostPerCbm,
            recommendedSalePerCbm,
            freightSalePerCbm,
            lines,
            subtotalCost,
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
        var dim = Math.Max(0m, line.LengthCm) * Math.Max(0m, line.WidthCm) * Math.Max(0m, line.HeightCm) * units / 1_000_000m;
        var weight = Math.Max(0m, line.TotalWeightKg) / 500m;
        return new CargoCalculationLine(line.Description?.Trim() ?? string.Empty, units, line.TotalWeightKg, dim, weight, Math.Max(dim, weight));
    }

    private static void AddDestinationLines(List<OwnLclQuoteLine> lines, string destination, decimal cbm, decimal destinationCostPerCbm)
    {
        switch (destination)
        {
            case "PA":
                AddLine(lines, "Destination Charge", "CBM", cbm, destinationCostPerCbm, 20m);
                AddLine(lines, "DMCE", "HBL", 1, 65m, 65m);
                AddLine(lines, "Manejos", "HBL", 1, 25m, 25m);
                AddLine(lines, "Zone Charge", "HBL", 1, 30m, 30m);
                break;
            case "CR":
                AddLine(lines, "Manejos", "HBL", 1, 65m, 65m);
                AddLine(lines, "Zone Charge", "HBL", 1, 50m, 50m);
                break;
            default:
                AddCentralAmericaLines(lines, destination, cbm);
                break;
        }
    }

    private static void AddCentralAmericaLines(List<OwnLclQuoteLine> lines, string destination, decimal cbm)
    {
        if (!CentralAmericaLandFreight.TryGetValue(destination, out var inland)) return;

        var warehousePerCbm = CostaRicaWarehouseOperation / CentralAmericaOperationBaseCbm;
        var inlandPerCbm = inland.Total / CentralAmericaOperationBaseCbm;
        AddLine(lines, "Almacenaje CRC", "CBM", cbm, warehousePerCbm, warehousePerCbm);
        AddLine(lines, inland.Label, "CBM", cbm, inlandPerCbm, inlandPerCbm);
    }

    private static void AddOriginLines(List<OwnLclQuoteLine> lines, string incoterm, decimal cbm, int sets, int hbl, decimal pickupCost, decimal pickupSale)
    {
        if (incoterm == "FOB") return;
        AddLine(lines, "CFS", "CBM", cbm, 8m, 8m);
        AddLine(lines, "CUSTOMS", "SET", sets, 15m, 25m);
        AddLine(lines, "DOC FEE", "HBL", hbl, 15m, 65m);
        AddLine(lines, "VGM", "HBL", hbl, 0m, 25m);
        AddLine(lines, "MANIFEST", "HBL", hbl, 15m, 25m);
        AddLine(lines, "WHSE FEE", "CBM", cbm, 12m, 12m);
        if (incoterm == "EXW") AddLine(lines, "Recolecta", "Flat", 1, Math.Max(0m, pickupCost), Math.Max(0m, pickupSale));
    }

    private static void AddLine(List<OwnLclQuoteLine> lines, string name, string basis, decimal quantity, decimal costUnit, decimal saleUnit)
    {
        lines.Add(new OwnLclQuoteLine(name, basis, quantity, costUnit, saleUnit, quantity * costUnit, quantity * saleUnit, quantity * (saleUnit - costUnit)));
    }

    private static async Task<decimal?> LoadHistoricalSaleAsync(int number, string destination, string polCode, ServiceDbContext db, CancellationToken ct)
    {
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sale_per_cbm
            FROM pricing."OwnLclHistoricalRates"
            WHERE consolidation_number=@number AND destination_code=@destination AND UPPER(pol_code)=UPPER(@pol)
            LIMIT 1;
            """;
        Add(command, "number", number);
        Add(command, "destination", destination);
        Add(command, "pol", NormalizeCode(polCode));
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : Convert.ToDecimal(value);
    }

    private static async Task<OwnLclConsolidationDto?> LoadAsync(Guid id, ServiceDbContext db, CancellationToken ct)
    {
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, consolidation_number, name, booking, etd, carrier_id, carrier_name, carrier_code,
                   container_id, container_name, container_code, pol_id, pol_name, pol_code,
                   panama_arrival_port_id, panama_arrival_port_name, panama_arrival_port_code,
                   pod_id, pod_name, pod_code,
                   ocean_freight, maximum_cbm, carrier_destination_cost_total, panama_to_cr_cost,
                   bunker_cost, cr_transfer_base_cbm, matrix_version, status, is_active
            FROM pricing."OwnLclConsolidations"
            WHERE id=@id AND is_active=TRUE
            LIMIT 1;
            """;
        Add(command, "id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadConsolidation(reader) : null;
    }

    private static OwnLclConsolidationDto ReadConsolidation(DbDataReader reader) => new(
        reader.GetGuid(0), reader.GetInt32(1), reader.GetString(2), GetNullableString(reader, 3), GetNullableDate(reader, 4),
        GetNullableGuid(reader, 5), GetNullableString(reader, 6), GetNullableString(reader, 7),
        GetNullableGuid(reader, 8), GetNullableString(reader, 9), GetNullableString(reader, 10),
        GetNullableGuid(reader, 11), GetNullableString(reader, 12), reader.GetString(13),
        GetNullableGuid(reader, 14), GetNullableString(reader, 15), GetNullableString(reader, 16),
        GetNullableGuid(reader, 17), GetNullableString(reader, 18), GetNullableString(reader, 19),
        reader.GetDecimal(20), reader.GetDecimal(21), reader.GetDecimal(22), reader.GetDecimal(23), reader.GetDecimal(24), reader.GetDecimal(25),
        reader.GetString(26), reader.GetString(27), reader.GetBoolean(28));

    private static object? Validate(string? booking, string? polCode, decimal oceanFreight, decimal? maximumCbm)
    {
        if (string.IsNullOrWhiteSpace(booking)) return new { code = "Pricing.OwnLclBookingRequired", message = "Ingrese el número de booking." };
        if (string.IsNullOrWhiteSpace(polCode)) return new { code = "Pricing.OwnLclPolRequired", message = "Seleccione el POL del consolidado." };
        if (oceanFreight <= 0) return new { code = "Pricing.OwnLclOceanFreightRequired", message = "El flete marítimo debe ser mayor a cero." };
        if (maximumCbm is <= 0) return new { code = "Pricing.OwnLclMaximumCbmInvalid", message = "El máximo CBM debe ser mayor a cero." };
        return null;
    }

    private static string? NormalizeDestination(string? value)
    {
        var normalized = NormalizeCode(value);
        if (normalized.Contains("COSTA RICA") || normalized is "CR" or "SJO" or "SAN JOSE" or "SAN JOSÉ") return "CR";
        if (normalized.Contains("PANAMA") || normalized.Contains("PANAMÁ") || normalized.Contains("CFZ") || normalized.Contains("CZF") || normalized is "PA") return "PA";
        if (normalized.Contains("NICARAGUA") || normalized.Contains("MANAGUA") || normalized is "NI") return "NI";
        if (normalized.Contains("HONDURAS") || normalized.Contains("SAN PEDRO SULA") || normalized is "HN") return "HN";
        if (normalized.Contains("GUATEMALA") || normalized is "GT") return "GT";
        if (normalized.Contains("EL SALVADOR") || normalized.Contains("SAN SALVADOR") || normalized is "SV") return "SV";
        return null;
    }

    private static decimal CeilingCent(decimal value) => Math.Ceiling(value * 100m) / 100m;
    private static string NormalizeCode(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? GetNullableString(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static Guid? GetNullableGuid(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    private static DateOnly? GetNullableDate(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : DateOnly.FromDateTime(reader.GetDateTime(ordinal));

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

public sealed record CreateOwnLclConsolidationRequest(
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
    decimal OceanFreight,
    decimal? MaximumCbm = 50m,
    decimal? CarrierDestinationCostTotal = 912m,
    decimal? PanamaToCostaRicaCost = 2140m,
    decimal? BunkerCost = 280m,
    decimal? CostaRicaTransferBaseCbm = 95m);

public sealed record UpdateOwnLclConsolidationRequest(
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
    decimal OceanFreight,
    decimal MaximumCbm,
    decimal CarrierDestinationCostTotal,
    decimal PanamaToCostaRicaCost,
    decimal BunkerCost,
    decimal CostaRicaTransferBaseCbm);

public sealed record CalculateOwnLclQuoteRequest(
    string DestinationCode,
    string Incoterm,
    IReadOnlyCollection<OwnLclCargoLineRequest> CargoLines,
    string? PolCode = null,
    decimal? SalePerCbm = null,
    int Sets = 1,
    int Hbl = 1,
    decimal PickupCost = 0m,
    decimal PickupSale = 0m,
    decimal Discount = 0m);

public sealed record OwnLclCargoLineRequest(
    string? Description,
    int Units,
    decimal TotalWeightKg,
    decimal LengthCm,
    decimal WidthCm,
    decimal HeightCm);

public sealed record CargoCalculationLine(string Description, int Units, decimal TotalWeightKg, decimal DimensionalCbm, decimal WeightCbm, decimal ChargeableCbm);
public sealed record OwnLclQuoteLine(string Name, string ChargeBasis, decimal Quantity, decimal CostUnit, decimal SaleUnit, decimal CostTotal, decimal SaleTotal, decimal Profit);

public sealed record OwnLclConsolidationDto(
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
    string MatrixVersion,
    string Status,
    bool IsActive);

public sealed record OwnLclQuoteCalculationDto(
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
    decimal CostaRicaTransferCostPerCbm,
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
