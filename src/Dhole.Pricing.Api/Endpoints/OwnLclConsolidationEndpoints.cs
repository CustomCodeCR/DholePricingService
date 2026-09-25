using System.Data;
using System.Data.Common;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class OwnLclConsolidationEndpoints
{
    private const decimal DefaultMaximumCbm = 50m;
    private const decimal DefaultFreightProfitPerCbm = 5.69m;
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
        group.MapPatch("/{id:guid}/name", RenameAsync)
            .RequireScope(PricingConstants.Scopes.OwnLclConsolidationCreate);
        group.MapPost("/", CreateAsync).RequireIdempotency().RequireScope(PricingConstants.Scopes.OwnLclConsolidationCreate);
        group.MapPut("/{id:guid}", UpdateAsync).RequireScope(PricingConstants.Scopes.OwnLclConsolidationCreate);
        group.MapPost("/{id:guid}/calculate", CalculateAsync).RequireScope(PricingConstants.Scopes.RateCreate);

        return app;
    }

    private static async Task<IResult> BrowseAsync(
        ServiceDbContext db,
        IRateHeaderRepository rateHeaders,
        CancellationToken ct
    )
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
                   bunker_cost, cr_transfer_base_cbm, freight_profit_per_cbm, matrix_version, status, is_active
            FROM pricing."OwnLclConsolidations"
            WHERE is_active = TRUE
            ORDER BY consolidation_number DESC;
            """;

        var result = new List<OwnLclConsolidationDto>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                result.Add(ReadConsolidation(reader));
        }

        var enriched = new List<OwnLclConsolidationDto>(result.Count);
        foreach (var row in result)
            enriched.Add(await WithCapacityAsync(row, rateHeaders, ct));

        return Results.Ok(enriched);
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        ServiceDbContext db,
        IRateHeaderRepository rateHeaders,
        CancellationToken ct
    )
    {
        var row = await LoadAsync(id, db, ct);
        if (row is null)
            return Results.NotFound();

        return Results.Ok(await WithCapacityAsync(row, rateHeaders, ct));
    }

    private static async Task<IResult> RenameAsync(
        Guid id,
        RenameOwnLclConsolidationRequest request,
        ServiceDbContext db,
        CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Results.BadRequest(new
            {
                code = "Pricing.OwnLclConsolidationNameRequired",
                message = "El nombre del consolidado es obligatorio.",
            });
        }

        if (name.Length > 200)
        {
            return Results.BadRequest(new
            {
                code = "Pricing.OwnLclConsolidationNameTooLong",
                message = "El nombre del consolidado no puede superar 200 caracteres.",
            });
        }

        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE pricing."OwnLclConsolidations"
            SET name=@name,
                updated_at_utc=now()
            WHERE id=@id AND is_active=TRUE;
            """;
        Add(command, "id", id);
        Add(command, "name", name);

        if (await command.ExecuteNonQueryAsync(ct) == 0)
            return Results.NotFound();

        return Results.Ok(new { id, name });
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
                     bunker_cost, cr_transfer_base_cbm, freight_profit_per_cbm, matrix_version, status, is_active, created_at_utc)
                VALUES
                    (@id, @number, @name, @booking, @etd, @carrier_id, @carrier_name, @carrier_code,
                     @container_id, @container_name, @container_code, @pol_id, @pol_name, @pol_code,
                     @ocean_freight, @maximum_cbm, @destination_cost, @panama_to_cr, @bunker,
                     @cr_base, @freight_profit_per_cbm, @version, 'Draft', TRUE, now());
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
            Add(command, "freight_profit_per_cbm", Math.Max(0m, request.FreightProfitPerCbm ?? DefaultFreightProfitPerCbm));
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
                freight_profit_per_cbm=COALESCE(@freight_profit_per_cbm, freight_profit_per_cbm),
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
        Add(command, "freight_profit_per_cbm", request.FreightProfitPerCbm.HasValue ? Math.Max(0m, request.FreightProfitPerCbm.Value) : null);

        return await command.ExecuteNonQueryAsync(ct) == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> CalculateAsync(Guid id, CalculateOwnLclQuoteRequest request, ServiceDbContext db, CancellationToken ct)
    {
        var consolidation = await LoadAsync(id, db, ct);
        if (consolidation is null) return Results.NotFound();
        if (consolidation.Etd.HasValue && CurrentBusinessDate() >= consolidation.Etd.Value)
        {
            return Results.Conflict(new
            {
                code = "Pricing.OwnLclEtdExpired",
                message = "El consolidado ya alcanzó o superó su ETD y no está disponible para nuevas cotizaciones.",
            });
        }

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
        var originPort = NormalizeOriginPort(request.PolCode ?? consolidation.PolCode);
        var originSurcharge = OriginSurcharges.GetValueOrDefault(originPort);
        var oceanCostWithOrigin = baseOceanCost + originSurcharge;
        var destinationCostPerCbm = consolidation.CarrierDestinationCostTotal / maximumCbm;
        var consolidationPricingLines = await LoadConsolidationPricingLinesAsync(id, destinationCostPerCbm, db, ct);
        var crTransferCostPerCbm = (consolidation.PanamaToCostaRicaCost + consolidation.BunkerCost) / Math.Max(1m, consolidation.CostaRicaTransferBaseCbm);

        // Igual que en el HTML: Panamá y Centroamérica venden el O/F por
        // separado de sus cargos de destino. Costa Rica mantiene el tramo hasta GAM
        // dentro del costo del flete base.
        var freightCostPerCbm = destination == "CR"
            ? oceanCostWithOrigin + destinationCostPerCbm + crTransferCostPerCbm
            : oceanCostWithOrigin;

        // La utilidad/CBM configurada pertenece al consolidado, no al destino.
        // Se aplica al Flete Internacional Marítimo para Panamá, Costa Rica y
        // el resto de Centroamérica de la misma manera.
        var configuredFreightProfitPerCbm = Math.Max(0m, consolidation.FreightProfitPerCbm);
        var recommendedSalePerCbm = freightCostPerCbm + configuredFreightProfitPerCbm;
        var freightSalePerCbm = recommendedSalePerCbm;

        var lines = new List<OwnLclQuoteLine>();
        AddLine(lines, "Flete Internacional Marítimo", "CBM", billableCbm, freightCostPerCbm, freightSalePerCbm);
        AddDestinationLines(
            lines,
            destination,
            billableCbm,
            destinationCostPerCbm,
            consolidationPricingLines);
        AddOriginLines(
            lines,
            incoterm,
            originPort,
            billableCbm,
            chargeableCbm,
            Math.Max(1, request.Sets),
            Math.Max(1, request.Hbl),
            consolidationPricingLines);

        var subtotalCost = lines.Sum(x => x.CostTotal);
        var subtotalSale = lines.Sum(x => x.SaleTotal);
        var discount = Math.Min(Math.Max(0m, request.Discount), subtotalSale);
        var finalSale = subtotalSale - discount;
        var profit = finalSale - subtotalCost;
        var profitPerCbm = profit / billableCbm;
        var profitPercentage = finalSale > 0 ? (profit / finalSale) * 100m : 0m;

        var oceanProfitPerCbm = freightSalePerCbm - freightCostPerCbm;
        var minimumProfit = configuredFreightProfitPerCbm;
        var meetsMinimum = Math.Abs(oceanProfitPerCbm - configuredFreightProfitPerCbm) <= 0.000001m;

        return Results.Ok(new OwnLclQuoteCalculationDto(
            consolidation.Id,
            consolidation.ConsolidationNumber,
            consolidation.Name,
            consolidation.MatrixVersion,
            originPort,
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

    private static void AddDestinationLines(
        List<OwnLclQuoteLine> lines,
        string destination,
        decimal cbm,
        decimal destinationCostPerCbm,
        IReadOnlyDictionary<string, (decimal CostUnit, decimal SaleUnit, decimal? CalculationBaseCbm)> pricingLines)
    {
        switch (destination)
        {
            case "PA":
                AddConfiguredDestinationLine(
                    lines,
                    pricingLines,
                    "PA_DESTINATION_CHARGE",
                    "Destination Charge",
                    "CBM",
                    cbm,
                    destinationCostPerCbm);
                AddConfiguredDestinationLine(lines, pricingLines, "PA_DMCE", "DMCE", "HBL", 1m);
                AddConfiguredDestinationLine(lines, pricingLines, "PA_HANDLING", "Handling", "HBL", 1m);
                AddConfiguredDestinationLine(lines, pricingLines, "PA_ZONE", "Zone Charge", "HBL", 1m);
                break;

            case "CR":
                AddConfiguredDestinationLine(lines, pricingLines, "CR_HANDLING", "Manejos", "HBL", 1m);
                AddConfiguredDestinationLine(lines, pricingLines, "CR_ZONE", "Zone Charge", "HBL", 1m);
                break;

            default:
                AddConfiguredDestinationLine(lines, pricingLines, "CA_TRANSSHIPMENT", "Transbordo", "CBM", cbm);
                AddConfiguredCalculatedInlandDestinationLine(
                    lines,
                    pricingLines,
                    destination switch
                    {
                        "NI" => "CA_INLAND_NI",
                        "HN" => "CA_INLAND_HN",
                        "GT" => "CA_INLAND_GT",
                        "SV" => "CA_INLAND_SV",
                        _ => throw new InvalidOperationException($"Destino centroamericano no soportado: {destination}."),
                    },
                    "Flete Terrestre",
                    cbm);
                AddConfiguredDestinationLine(lines, pricingLines, "CA_STUFFING", "Stuffing", "CBM", cbm);
                AddConfiguredDestinationLine(lines, pricingLines, "CA_DOCUMENTATION", "Documentación", "HBL", 1m);
                AddConfiguredDestinationLine(lines, pricingLines, "CA_HANDLING", "Manejos", "HBL", 1m);
                AddConfiguredDestinationLine(lines, pricingLines, "CA_DESTINATION_HANDLING", "Manejos en Destino", "HBL", 1m);
                break;
        }
    }

    private static void AddConfiguredCalculatedInlandDestinationLine(
        List<OwnLclQuoteLine> lines,
        IReadOnlyDictionary<string, (decimal CostUnit, decimal SaleUnit, decimal? CalculationBaseCbm)> pricingLines,
        string lineKey,
        string name,
        decimal quantity)
    {
        if (!pricingLines.TryGetValue(lineKey, out var values)) return;
        var baseCbm = values.CalculationBaseCbm is > 0m ? values.CalculationBaseCbm.Value : 70m;
        var costPerCbm = Math.Max(0m, values.CostUnit) / baseCbm;
        AddLine(lines, name, "CBM", quantity, costPerCbm, Math.Max(0m, values.SaleUnit));
    }

    private static void AddConfiguredDestinationLine(
        List<OwnLclQuoteLine> lines,
        IReadOnlyDictionary<string, (decimal CostUnit, decimal SaleUnit, decimal? CalculationBaseCbm)> pricingLines,
        string lineKey,
        string name,
        string basis,
        decimal quantity,
        decimal? fallbackCost = null)
    {
        if (!pricingLines.TryGetValue(lineKey, out var values))
            return;

        var cost = values.CostUnit;
        if (lineKey.Equals("CR_HANDLING", StringComparison.OrdinalIgnoreCase)
            || lineKey.Equals("CR_ZONE", StringComparison.OrdinalIgnoreCase))
        {
            cost = 0m;
        }
        else if (lineKey.Equals("PA_DESTINATION_CHARGE", StringComparison.OrdinalIgnoreCase)
            && cost <= 0m
            && fallbackCost.HasValue)
        {
            cost = fallbackCost.Value;
        }

        AddLine(
            lines,
            name,
            basis,
            quantity,
            Math.Max(0m, cost),
            Math.Max(0m, values.SaleUnit));
    }

    private static void AddOriginLines(
        List<OwnLclQuoteLine> lines,
        string incoterm,
        string originPort,
        decimal billableCbm,
        decimal actualChargeableCbm,
        int sets,
        int hbl,
        IReadOnlyDictionary<string, (decimal CostUnit, decimal SaleUnit, decimal? CalculationBaseCbm)> pricingLines)
    {
        if (incoterm == "FOB") return;

        // CFS is charged on the actual CBM and has no 1-CBM minimum.
        AddConfiguredOriginLine(lines, pricingLines, "ORIGIN_CFS", "CFS", "CBM", actualChargeableCbm);
        AddConfiguredOriginLine(lines, pricingLines, "ORIGIN_CUSTOMS", "CUSTOMS", "SET", sets);
        AddConfiguredOriginLine(lines, pricingLines, "ORIGIN_DOC", "DOC FEE", "HBL", hbl);
        AddConfiguredOriginLine(lines, pricingLines, "ORIGIN_VGM", "VGM", "HBL", hbl);
        AddConfiguredOriginLine(lines, pricingLines, "ORIGIN_MANIFEST", "MANIFEST", "HBL", hbl);

        // WHSE FEE solo aplica a Shanghai. En Ningbo, Qingdao y los demás puertos
        // de la matriz China -> Shanghai, el componente de warehouse ya está incluido
        // dentro del diferencial de origen para evitar cobrarlo dos veces.
        if (string.Equals(originPort, "SHANGHAI", StringComparison.OrdinalIgnoreCase))
            AddConfiguredOriginLine(lines, pricingLines, "ORIGIN_WHSE", "WHSE FEE", "CBM", billableCbm);

        if (incoterm == "EXW")
            AddConfiguredOriginLine(lines, pricingLines, "ORIGIN_PICK_UP", "PICK UP", "Flat", 1m);
    }

    private static void AddConfiguredOriginLine(
        List<OwnLclQuoteLine> lines,
        IReadOnlyDictionary<string, (decimal CostUnit, decimal SaleUnit, decimal? CalculationBaseCbm)> pricingLines,
        string lineKey,
        string name,
        string basis,
        decimal quantity)
    {
        if (!pricingLines.TryGetValue(lineKey, out var values)) return;
        AddLine(lines, name, basis, quantity, Math.Max(0m, values.CostUnit), Math.Max(0m, values.SaleUnit));
    }

    private static void AddLine(List<OwnLclQuoteLine> lines, string name, string basis, decimal quantity, decimal costUnit, decimal saleUnit)
    {
        lines.Add(new OwnLclQuoteLine(name, basis, quantity, costUnit, saleUnit, quantity * costUnit, quantity * saleUnit, quantity * (saleUnit - costUnit)));
    }

    private static async Task<IReadOnlyDictionary<string, (decimal CostUnit, decimal SaleUnit, decimal? CalculationBaseCbm)>> LoadConsolidationPricingLinesAsync(
        Guid consolidationId,
        decimal destinationCostPerCbm,
        ServiceDbContext db,
        CancellationToken ct)
    {
        Dictionary<string, (decimal CostUnit, decimal SaleUnit, decimal? CalculationBaseCbm)> values =
            OwnLclPricingLineCatalog.All.ToDictionary(
                definition => definition.LineKey,
                definition => (
                    CostUnit: definition.LineKey == "CA_TRANSSHIPMENT"
                        ? destinationCostPerCbm + 9m
                        : definition.DefaultCostUnit ?? 0m,
                    SaleUnit: definition.DefaultSaleUnit,
                    CalculationBaseCbm: definition.LineKey.StartsWith("CA_INLAND_", StringComparison.OrdinalIgnoreCase)
                        ? (decimal?)70m
                        : null),
                StringComparer.OrdinalIgnoreCase);
        var hasStoredTransshipment = false;

        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT line_key, cost_unit, sale_unit, calculation_base_cbm
            FROM pricing."OwnLclConsolidationPricingLines"
            WHERE consolidation_id=@consolidation_id;
            """;
        Add(command, "consolidation_id", consolidationId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var key = reader.GetString(0);
            if (!values.ContainsKey(key)) continue;
            if (key.Equals("CA_TRANSSHIPMENT", StringComparison.OrdinalIgnoreCase))
                hasStoredTransshipment = true;
            values[key] = (
                reader.GetDecimal(1),
                reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetDecimal(3));
        }

        if (!hasStoredTransshipment)
        {
            var panamaSale = values["PA_DESTINATION_CHARGE"].SaleUnit;
            values["CA_TRANSSHIPMENT"] = (destinationCostPerCbm + 9m, panamaSale + 9m, null);
        }

        return values;
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

    private static async Task<OwnLclConsolidationDto> WithCapacityAsync(
        OwnLclConsolidationDto row,
        IRateHeaderRepository rateHeaders,
        CancellationToken ct
    )
    {
        var approvedCbm = await rateHeaders.GetAcceptedOwnLclCbmAsync(
            row.Id,
            row.ConsolidationNumber,
            ct
        );
        var remainingCbm = Math.Max(0m, row.MaximumCbm - approvedCbm);

        return row with
        {
            ApprovedCbm = approvedCbm,
            RemainingCbm = remainingCbm,
            CapacityReached = remainingCbm <= 0m,
        };
    }

    private static DateOnly CurrentBusinessDate()
    {
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Costa_Rica");
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
            return DateOnly.FromDateTime(localNow);
        }
        catch (TimeZoneNotFoundException)
        {
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }
        catch (InvalidTimeZoneException)
        {
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }
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
                   bunker_cost, cr_transfer_base_cbm, freight_profit_per_cbm, matrix_version, status, is_active
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
        reader.GetDecimal(26), reader.GetString(27), reader.GetString(28), reader.GetBoolean(29));

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

    private static string NormalizeOriginPort(string? value)
    {
        var normalized = NormalizeCode(value);
        if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;

        foreach (var port in OriginSurcharges.Keys)
        {
            if (normalized.Equals(port, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith($"{port},", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith($"{port} ", StringComparison.OrdinalIgnoreCase))
            {
                return port;
            }
        }

        return normalized;
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

public sealed record RenameOwnLclConsolidationRequest(string Name);

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
    decimal? CostaRicaTransferBaseCbm = 95m,
    decimal? FreightProfitPerCbm = 5.69m);

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
    decimal CostaRicaTransferBaseCbm,
    decimal? FreightProfitPerCbm = null);

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
    decimal FreightProfitPerCbm,
    string MatrixVersion,
    string Status,
    bool IsActive,
    decimal ApprovedCbm = 0m,
    decimal RemainingCbm = 0m,
    bool CapacityReached = false);

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
