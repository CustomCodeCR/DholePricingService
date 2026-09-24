using System.Data;
using System.Data.Common;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class OwnLclFobScenarioEndpoints
{
    private const decimal MinimumCentralAmericaProfitPerCbm = 5.69m;
    private const decimal MinimumPanamaProfitPerCbm = 3.40m;
    private const decimal PanamaAndCentralAmericaFreightSalePerCbm = 164m;
    private const decimal CostaRicaFreightSalePerCbm = 210m;
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

    private static readonly IReadOnlyDictionary<string, (string Name, decimal Inland)> Destinations =
        new Dictionary<string, (string Name, decimal Inland)>(StringComparer.OrdinalIgnoreCase)
        {
            ["PA"] = ("Panamá", 0m),
            ["CR"] = ("Costa Rica", 0m),
            ["NI"] = ("Nicaragua", 1150m),
            ["HN"] = ("Honduras", 1825m),
            ["SV"] = ("El Salvador", 2200m),
            ["GT"] = ("Guatemala", 2450m),
        };

    public static IEndpointRouteBuilder MapOwnLclFobScenarioEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/own-lcl-consolidations/{id:guid}")
            .WithTags("Own LCL FOB scenarios")
            .RequireAuthorization();

        group.MapGet("/fob-scenarios", GetScenariosAsync)
            .RequireScope(PricingConstants.Scopes.RateView);
        group.MapPut("/fob-scenarios", SaveScenariosAsync)
            .RequireScope(PricingConstants.Scopes.OwnLclConsolidationCreate);
        group.MapPut("/cost-overrides", SaveCostOverridesAsync)
            .RequireScope(PricingConstants.Scopes.OwnLclConsolidationCreate);
        group.MapGet("/pricing-lines", GetPricingLinesAsync)
            .RequireScope(PricingConstants.Scopes.RateView);
        group.MapPut("/pricing-lines", SavePricingLinesAsync)
            .RequireScope(PricingConstants.Scopes.OwnLclConsolidationCreate);

        return app;
    }

    private static async Task<IResult> GetScenariosAsync(Guid id, ServiceDbContext db, CancellationToken ct)
    {
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);

        int consolidationNumber;
        string matrixVersion;
        DateOnly? validTo;
        decimal oceanFreight;
        decimal maximumCbm;
        decimal destinationCost;
        decimal panamaToCr;
        decimal bunker;
        decimal crBase;
        decimal freightProfitPerCbm;
        string polCode;
        string? polName;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT consolidation_number, matrix_version, etd, ocean_freight, maximum_cbm,
                       carrier_destination_cost_total, panama_to_cr_cost, bunker_cost, cr_transfer_base_cbm,
                       freight_profit_per_cbm, pol_code, pol_name
                FROM pricing."OwnLclConsolidations"
                WHERE id=@id AND is_active=TRUE
                LIMIT 1;
                """;
            Add(command, "id", id);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return Results.NotFound();

            consolidationNumber = reader.GetInt32(0);
            matrixVersion = reader.GetString(1);
            validTo = reader.IsDBNull(2) ? null : DateOnly.FromDateTime(reader.GetDateTime(2));
            oceanFreight = reader.GetDecimal(3);
            maximumCbm = Math.Max(0.01m, reader.GetDecimal(4));
            destinationCost = reader.GetDecimal(5);
            panamaToCr = reader.GetDecimal(6);
            bunker = reader.GetDecimal(7);
            crBase = Math.Max(0.01m, reader.GetDecimal(8));
            freightProfitPerCbm = Math.Max(0m, reader.GetDecimal(9));
            polCode = reader.GetString(10);
            polName = reader.IsDBNull(11) ? null : reader.GetString(11);
        }

        if (OwnLclPricingLineCatalog.IsMiamiOrigin(polCode, polName))
        {
            return Results.Ok(new OwnLclFobScenarioMatrixDto(
                id,
                consolidationNumber,
                matrixVersion,
                validTo,
                oceanFreight,
                maximumCbm,
                destinationCost,
                panamaToCr,
                bunker,
                crBase,
                Array.Empty<OwnLclFobScenarioCountryDto>()));
        }

        var sales = new Dictionary<(string Destination, string Pol), decimal>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT UPPER(destination_code), UPPER(pol_code), sale_per_cbm
                FROM pricing."OwnLclHistoricalRates"
                WHERE consolidation_number=@number;
                """;
            Add(command, "number", consolidationNumber);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                sales[(reader.GetString(0), reader.GetString(1))] = reader.GetDecimal(2);
        }

        var baseOcean = oceanFreight / maximumCbm;
        var destinationPerCbm = destinationCost / maximumCbm;
        var pricingLineOverrides = await LoadPricingLineOverridesAsync(connection, id, ct);
        var paDestinationLine = ResolvePricingLine(
            pricingLineOverrides,
            "PA_DESTINATION_CHARGE",
            destinationPerCbm);
        var crTransferPerCbm = (panamaToCr + bunker) / crBase;
        var warehousePerCbm = CostaRicaWarehouseOperation / CentralAmericaOperationBaseCbm;

        var countries = Destinations.Select(destination =>
        {
            var code = destination.Key;

            var ports = OriginSurcharges.Select(origin =>
            {
                // El escenario FOB representa únicamente el flete marítimo.
                var cost = code == "CR"
                    ? CeilingCent(baseOcean + origin.Value + destinationPerCbm + crTransferPerCbm)
                    : CeilingCent(baseOcean + origin.Value);

                // La utilidad configurada en el consolidado se suma al costo/CBM
                // para Panamá, Costa Rica y Centroamérica sin excepciones.
                var recommended = cost + freightProfitPerCbm;
                var sale = recommended;

                return new OwnLclFobScenarioPortDto(origin.Key, cost, sale, recommended, origin.Value);
            }).ToArray();

            return new OwnLclFobScenarioCountryDto(code, destination.Value.Name, ports);
        }).ToArray();

        return Results.Ok(new OwnLclFobScenarioMatrixDto(
            id,
            consolidationNumber,
            matrixVersion,
            validTo,
            oceanFreight,
            maximumCbm,
            destinationCost,
            panamaToCr,
            bunker,
            crBase,
            countries));
    }

    private static async Task<IResult> SaveScenariosAsync(
        Guid id,
        SaveOwnLclFobScenariosRequest request,
        ServiceDbContext db,
        CancellationToken ct)
    {
        if (request.Rows.Count == 0)
            return Results.BadRequest(new { code = "Pricing.OwnLclScenariosRequired", message = "Agregue al menos un escenario FOB." });

        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);

        int consolidationNumber;
        string matrixVersion;
        DateOnly? validTo;
        decimal oceanFreight;
        decimal maximumCbm;
        decimal freightProfitPerCbm;
        string polCode;
        string? polName;
        await using (var lookup = connection.CreateCommand())
        {
            lookup.CommandText = "SELECT consolidation_number, matrix_version, etd, ocean_freight, maximum_cbm, freight_profit_per_cbm, pol_code, pol_name FROM pricing.\"OwnLclConsolidations\" WHERE id=@id AND is_active=TRUE LIMIT 1;";
            Add(lookup, "id", id);
            await using var reader = await lookup.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return Results.NotFound();
            consolidationNumber = reader.GetInt32(0);
            matrixVersion = reader.GetString(1);
            validTo = reader.IsDBNull(2) ? null : DateOnly.FromDateTime(reader.GetDateTime(2));
            oceanFreight = reader.GetDecimal(3);
            maximumCbm = Math.Max(0.01m, reader.GetDecimal(4));
            freightProfitPerCbm = Math.Max(0m, reader.GetDecimal(5));
            polCode = reader.GetString(6);
            polName = reader.IsDBNull(7) ? null : reader.GetString(7);
        }

        if (OwnLclPricingLineCatalog.IsMiamiOrigin(polCode, polName))
        {
            return Results.BadRequest(new
            {
                code = "Pricing.OwnLclMiamiDoesNotUseChinaFobScenarios",
                message = "La matriz Miami es independiente y no utiliza escenarios FOB por puertos de China.",
            });
        }

        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        foreach (var row in request.Rows)
        {
            var destination = Normalize(row.DestinationCode);
            var pol = Normalize(row.PolCode);
            if (!Destinations.ContainsKey(destination) || !OriginSurcharges.ContainsKey(pol) || row.SalePerCbm < 0m)
            {
                await tx.RollbackAsync(ct);
                return Results.BadRequest(new { code = "Pricing.OwnLclScenarioInvalid", message = $"Escenario inválido: {destination}/{pol}." });
            }

            await using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = """
                INSERT INTO pricing."OwnLclHistoricalRates"
                    (id, consolidation_number, destination_code, pol_code, sale_per_cbm, valid_to, version)
                VALUES
                    (gen_random_uuid(), @number, @destination, @pol, @sale, @valid_to, @version)
                ON CONFLICT (consolidation_number, destination_code, pol_code)
                DO UPDATE SET sale_per_cbm=EXCLUDED.sale_per_cbm, valid_to=EXCLUDED.valid_to, version=EXCLUDED.version;
                """;
            Add(command, "number", consolidationNumber);
            Add(command, "destination", destination);
            Add(command, "pol", pol);
            var salePerCbm = IsCentralAmericaDestination(destination)
                ? (oceanFreight / maximumCbm) + OriginSurcharges[pol] + freightProfitPerCbm
                : row.SalePerCbm;
            Add(command, "sale", salePerCbm);
            Add(command, "valid_to", validTo);
            Add(command, "version", matrixVersion);
            await command.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SaveCostOverridesAsync(
        Guid id,
        SaveOwnLclCostOverridesRequest request,
        ServiceDbContext db,
        CancellationToken ct)
    {
        if (request.OceanFreight <= 0m || request.MaximumCbm <= 0m
            || request.CarrierDestinationCostTotal < 0m || request.PanamaToCostaRicaCost < 0m
            || request.BunkerCost < 0m || request.CostaRicaTransferBaseCbm <= 0m)
        {
            return Results.BadRequest(new { code = "Pricing.OwnLclCostOverrideInvalid", message = "Los costos del consolidado deben ser válidos y no negativos." });
        }

        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE pricing."OwnLclConsolidations"
            SET ocean_freight=@ocean,
                maximum_cbm=@maximum_cbm,
                carrier_destination_cost_total=@destination,
                panama_to_cr_cost=@land_freight,
                bunker_cost=@bunker,
                cr_transfer_base_cbm=@cr_base,
                updated_at_utc=now()
            WHERE id=@id AND is_active=TRUE;
            """;
        Add(command, "id", id);
        Add(command, "ocean", request.OceanFreight);
        Add(command, "maximum_cbm", request.MaximumCbm);
        Add(command, "destination", request.CarrierDestinationCostTotal);
        Add(command, "land_freight", request.PanamaToCostaRicaCost);
        Add(command, "bunker", request.BunkerCost);
        Add(command, "cr_base", request.CostaRicaTransferBaseCbm);

        return await command.ExecuteNonQueryAsync(ct) == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> GetPricingLinesAsync(
        Guid id,
        ServiceDbContext db,
        CancellationToken ct)
    {
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);

        decimal destinationCostPerCbm;
        string polCode;
        string? polName;
        await using (var lookup = connection.CreateCommand())
        {
            lookup.CommandText = """
                SELECT carrier_destination_cost_total, maximum_cbm, pol_code, pol_name
                FROM pricing."OwnLclConsolidations"
                WHERE id=@id AND is_active=TRUE
                LIMIT 1;
                """;
            Add(lookup, "id", id);
            await using var reader = await lookup.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return Results.NotFound();
            destinationCostPerCbm = reader.GetDecimal(0) / Math.Max(0.01m, reader.GetDecimal(1));
            polCode = reader.GetString(2);
            polName = reader.IsDBNull(3) ? null : reader.GetString(3);
        }

        var definitions = OwnLclPricingLineCatalog.ForOrigin(polCode, polName);
        var isMiami = OwnLclPricingLineCatalog.IsMiamiOrigin(polCode, polName);
        var overrides = await LoadPricingLineOverridesAsync(connection, id, ct);
        var panamaDestination = isMiami
            ? (Cost: 0m, Sale: 0m, CalculationBaseCbm: (decimal?)null)
            : ResolvePricingLine(overrides, "PA_DESTINATION_CHARGE", destinationCostPerCbm);

        var rows = definitions.Select(definition =>
        {
            if (overrides.TryGetValue(definition.LineKey, out var stored))
            {
                return new OwnLclPricingLineDto(
                    definition.LineKey,
                    definition.Scope,
                    definition.Name,
                    definition.ChargeBasis,
                    IsCostaRicaSaleOnlyLine(definition.LineKey) ? 0m : stored.Cost,
                    stored.Sale,
                    stored.CalculationBaseCbm);
            }

            var defaultCost = !isMiami
                ? definition.LineKey switch
                {
                    "PA_DESTINATION_CHARGE" => destinationCostPerCbm,
                    "CA_TRANSSHIPMENT" => panamaDestination.Cost + 9m,
                    _ => definition.DefaultCostUnit ?? 0m,
                }
                : definition.DefaultCostUnit ?? 0m;

            var defaultSale = !isMiami && definition.LineKey == "CA_TRANSSHIPMENT"
                ? panamaDestination.Sale + 9m
                : definition.DefaultSaleUnit;
            var defaultBase = IsCentralAmericaInlandLine(definition.LineKey) ? 70m : (decimal?)null;

            return new OwnLclPricingLineDto(
                definition.LineKey,
                definition.Scope,
                definition.Name,
                definition.ChargeBasis,
                defaultCost,
                defaultSale,
                defaultBase);
        }).ToArray();

        return Results.Ok(rows);
    }

    private static async Task<IResult> SavePricingLinesAsync(
        Guid id,
        SaveOwnLclPricingLinesRequest request,
        ServiceDbContext db,
        CancellationToken ct)
    {
        if (request.Rows.Count == 0)
            return Results.BadRequest(new { code = "Pricing.OwnLclPricingLinesRequired", message = "Agregue al menos una línea de costo/venta." });

        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);

        string polCode;
        string? polName;
        await using (var lookup = connection.CreateCommand())
        {
            lookup.CommandText = """
                SELECT pol_code, pol_name
                FROM pricing."OwnLclConsolidations"
                WHERE id=@id AND is_active=TRUE
                LIMIT 1;
                """;
            Add(lookup, "id", id);
            await using var reader = await lookup.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return Results.NotFound();
            polCode = reader.GetString(0);
            polName = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        var allowedDefinitions = OwnLclPricingLineCatalog.ForOrigin(polCode, polName);
        var allowedKeys = allowedDefinitions
            .Select(definition => definition.LineKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var normalized = request.Rows
            .Select(row =>
            {
                var lineKey = Normalize(row.LineKey);
                return row with
                {
                    LineKey = lineKey,
                    CostUnit = IsCostaRicaSaleOnlyLine(lineKey) ? 0m : row.CostUnit,
                };
            })
            .ToArray();
        if (normalized.Select(row => row.LineKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length
            || normalized.Any(row =>
                !allowedKeys.Contains(row.LineKey)
                || row.CostUnit < 0m
                || row.SaleUnit < 0m
                || (IsCentralAmericaInlandLine(row.LineKey) && (!row.CalculationBaseCbm.HasValue || row.CalculationBaseCbm.Value <= 0m))))
        {
            return Results.BadRequest(new
            {
                code = "Pricing.OwnLclPricingLineInvalid",
                message = OwnLclPricingLineCatalog.IsMiamiOrigin(polCode, polName)
                    ? "Las líneas no pertenecen a la matriz Miami o contienen valores inválidos."
                    : "Las líneas no pertenecen a la matriz China. Los fletes terrestres de Centroamérica requieren costo total, CBM base mayor a cero y venta por CBM.",
            });
        }

        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        foreach (var row in normalized)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = """
                INSERT INTO pricing."OwnLclConsolidationPricingLines"
                    (id, consolidation_id, line_key, cost_unit, sale_unit, calculation_base_cbm, updated_at_utc)
                VALUES
                    (gen_random_uuid(), @id, @key, @cost, @sale, @base_cbm, now())
                ON CONFLICT (consolidation_id, line_key)
                DO UPDATE SET
                    cost_unit=EXCLUDED.cost_unit,
                    sale_unit=EXCLUDED.sale_unit,
                    calculation_base_cbm=EXCLUDED.calculation_base_cbm,
                    updated_at_utc=now();
                """;
            Add(command, "id", id);
            Add(command, "key", row.LineKey);
            Add(command, "cost", row.CostUnit);
            Add(command, "sale", row.SaleUnit);
            Add(command, "base_cbm", IsCentralAmericaInlandLine(row.LineKey) ? row.CalculationBaseCbm : null);
            await command.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<Dictionary<string, (decimal Cost, decimal Sale, decimal? CalculationBaseCbm)>> LoadPricingLineOverridesAsync(
        DbConnection connection,
        Guid id,
        CancellationToken ct)
    {
        var result = new Dictionary<string, (decimal Cost, decimal Sale, decimal? CalculationBaseCbm)>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT line_key, cost_unit, sale_unit, calculation_base_cbm
            FROM pricing."OwnLclConsolidationPricingLines"
            WHERE consolidation_id=@id;
            """;
        Add(command, "id", id);
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

    private static (decimal Cost, decimal Sale, decimal? CalculationBaseCbm) ResolvePricingLine(
        IReadOnlyDictionary<string, (decimal Cost, decimal Sale, decimal? CalculationBaseCbm)> overrides,
        string lineKey,
        decimal fallbackCost)
    {
        if (overrides.TryGetValue(lineKey, out var stored))
        {
            return IsCostaRicaSaleOnlyLine(lineKey)
                ? (0m, stored.Sale, stored.CalculationBaseCbm)
                : stored;
        }

        var definition = OwnLclPricingLineCatalog.Find(lineKey)
            ?? throw new InvalidOperationException($"Línea LCL propia desconocida: {lineKey}.");
        return (
            IsCostaRicaSaleOnlyLine(lineKey) ? 0m : definition.DefaultCostUnit ?? fallbackCost,
            definition.DefaultSaleUnit,
            IsCentralAmericaInlandLine(lineKey) ? 70m : null);
    }

    private static bool IsCostaRicaSaleOnlyLine(string? lineKey) =>
        lineKey is "CR_HANDLING" or "CR_ZONE";

    private static bool IsCentralAmericaInlandLine(string? lineKey) =>
        lineKey is "CA_INLAND_NI" or "CA_INLAND_HN" or "CA_INLAND_GT" or "CA_INLAND_SV";

    private static bool IsCentralAmericaDestination(string? destination) =>
        destination is "NI" or "HN" or "GT" or "SV";

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    private static decimal CeilingCent(decimal value) => Math.Ceiling(value * 100m) / 100m;

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

public sealed record OwnLclFobScenarioPortDto(
    string PolCode,
    decimal CostPerCbm,
    decimal SalePerCbm,
    decimal RecommendedSalePerCbm,
    decimal OriginSurchargePerCbm);

public sealed record OwnLclFobScenarioCountryDto(
    string DestinationCode,
    string DestinationName,
    IReadOnlyCollection<OwnLclFobScenarioPortDto> Ports);

public sealed record OwnLclFobScenarioMatrixDto(
    Guid ConsolidationId,
    int ConsolidationNumber,
    string MatrixVersion,
    DateOnly? ValidTo,
    decimal OceanFreight,
    decimal MaximumCbm,
    decimal CarrierDestinationCostTotal,
    decimal PanamaToCostaRicaCost,
    decimal BunkerCost,
    decimal CostaRicaTransferBaseCbm,
    IReadOnlyCollection<OwnLclFobScenarioCountryDto> Countries);

public sealed record SaveOwnLclFobScenarioRowRequest(string DestinationCode, string PolCode, decimal SalePerCbm);
public sealed record SaveOwnLclFobScenariosRequest(IReadOnlyCollection<SaveOwnLclFobScenarioRowRequest> Rows);
public sealed record SaveOwnLclCostOverridesRequest(
    decimal OceanFreight,
    decimal MaximumCbm,
    decimal CarrierDestinationCostTotal,
    decimal PanamaToCostaRicaCost,
    decimal BunkerCost,
    decimal CostaRicaTransferBaseCbm);

public sealed record OwnLclPricingLineDto(
    string LineKey,
    string Scope,
    string Name,
    string ChargeBasis,
    decimal CostUnit,
    decimal SaleUnit,
    decimal? CalculationBaseCbm);

public sealed record SaveOwnLclPricingLineRequest(
    string LineKey,
    decimal CostUnit,
    decimal SaleUnit,
    decimal? CalculationBaseCbm);
public sealed record SaveOwnLclPricingLinesRequest(IReadOnlyCollection<SaveOwnLclPricingLineRequest> Rows);
