from pathlib import Path


def rep(path: str, old: str, new: str, label: str):
    p = Path(path)
    text = p.read_text()
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected one {label} anchor, found {count}")
    p.write_text(text.replace(old, new, 1))


catalog = Path('src/Dhole.Pricing.Api/Endpoints/OwnLclPricingLineCatalog.cs')
catalog.write_text('''namespace Dhole.Pricing.Api.Endpoints;

internal sealed record OwnLclPricingLineDefinition(
    string LineKey,
    string Scope,
    string Name,
    string ChargeBasis,
    decimal? DefaultCostUnit,
    decimal DefaultSaleUnit);

internal static class OwnLclPricingLineCatalog
{
    public static readonly IReadOnlyList<OwnLclPricingLineDefinition> All =
    [
        new("PA_DESTINATION_CHARGE", "PA", "Destination Charge", "CBM", null, 20m),
        new("PA_DMCE", "PA", "DMCE", "HBL", 65m, 65m),
        new("PA_HANDLING", "PA", "Handling", "HBL", 25m, 25m),
        new("PA_ZONE", "PA", "Zone Charge", "HBL", 30m, 30m),
        new("CR_HANDLING", "CR", "Manejos", "HBL", 65m, 65m),
        new("CR_ZONE", "CR", "Zone Charge", "HBL", 50m, 50m),
        new("CA_DOCUMENTATION", "CA", "Documentación", "HBL", 0m, 65m),
        new("CA_ZONE", "CA", "Zone Charge", "HBL", 0m, 65m),
        new("CA_HANDLING", "CA", "Manejos destino", "HBL", 0m, 50m),
        new("ORIGIN_CFS", "ORIGIN", "CFS", "CBM", 8m, 8m),
        new("ORIGIN_WHSE", "ORIGIN", "WHSE FEE", "CBM", 12m, 12m),
        new("ORIGIN_CUSTOMS", "ORIGIN", "CUSTOMS", "SET", 15m, 25m),
        new("ORIGIN_DOC", "ORIGIN", "DOC FEE", "HBL", 15m, 65m),
        new("ORIGIN_VGM", "ORIGIN", "VGM", "HBL", 0m, 25m),
        new("ORIGIN_MANIFEST", "ORIGIN", "MANIFEST", "HBL", 15m, 25m),
    ];

    public static OwnLclPricingLineDefinition? Find(string? lineKey) =>
        All.FirstOrDefault(item => string.Equals(item.LineKey, lineKey?.Trim(), StringComparison.OrdinalIgnoreCase));
}
''')

migration = Path('src/Dhole.Pricing.Persistence/Migrations/20260903210000_AddOwnLclConsolidationPricingLines.cs')
migration.write_text('''using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260903210000_AddOwnLclConsolidationPricingLines")]
public sealed class AddOwnLclConsolidationPricingLines : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS pricing."OwnLclConsolidationPricingLines" (
                id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                consolidation_id uuid NOT NULL,
                line_key varchar(80) NOT NULL,
                cost_unit numeric(18,6) NOT NULL DEFAULT 0,
                sale_unit numeric(18,6) NOT NULL DEFAULT 0,
                updated_at_utc timestamp with time zone NOT NULL DEFAULT now(),
                CONSTRAINT fk_own_lcl_pricing_lines_consolidation
                    FOREIGN KEY (consolidation_id)
                    REFERENCES pricing."OwnLclConsolidations"(id)
                    ON DELETE CASCADE,
                CONSTRAINT uq_own_lcl_pricing_lines UNIQUE (consolidation_id, line_key)
            );

            CREATE INDEX IF NOT EXISTS ix_own_lcl_pricing_lines_consolidation
                ON pricing."OwnLclConsolidationPricingLines" (consolidation_id);
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS pricing.\"OwnLclConsolidationPricingLines\";");
    }
}
''')

fob = 'src/Dhole.Pricing.Api/Endpoints/OwnLclFobScenarioEndpoints.cs'
rep(
    fob,
    '''        group.MapPut("/cost-overrides", SaveCostOverridesAsync)
            .RequireScope(PricingConstants.Scopes.OwnLclConsolidationCreate);

        return app;''',
    '''        group.MapPut("/cost-overrides", SaveCostOverridesAsync)
            .RequireScope(PricingConstants.Scopes.OwnLclConsolidationCreate);
        group.MapGet("/pricing-lines", GetPricingLinesAsync)
            .RequireScope(PricingConstants.Scopes.RateView);
        group.MapPut("/pricing-lines", SavePricingLinesAsync)
            .RequireScope(PricingConstants.Scopes.OwnLclConsolidationCreate);

        return app;''',
    'pricing line routes',
)
rep(
    fob,
    '''        var baseOcean = oceanFreight / maximumCbm;
        var destinationPerCbm = destinationCost / maximumCbm;
        var crTransferPerCbm = (panamaToCr + bunker) / crBase;''',
    '''        var baseOcean = oceanFreight / maximumCbm;
        var destinationPerCbm = destinationCost / maximumCbm;
        var pricingLineOverrides = await LoadPricingLineOverridesAsync(connection, id, ct);
        var paDestinationLine = ResolvePricingLine(
            pricingLineOverrides,
            "PA_DESTINATION_CHARGE",
            destinationPerCbm);
        var crTransferPerCbm = (panamaToCr + bunker) / crBase;''',
    'scenario pricing line load',
)
rep(
    fob,
    '''            var routeDestinationCost = code == "PA"
                ? destinationPerCbm
                : destinationPerCbm + crTransferPerCbm + (code is "NI" or "HN" or "SV" or "GT" ? warehousePerCbm + inlandPerCbm : 0m);''',
    '''            var routeDestinationCost = code == "PA"
                ? paDestinationLine.Cost
                : destinationPerCbm + crTransferPerCbm + (code is "NI" or "HN" or "SV" or "GT" ? warehousePerCbm + inlandPerCbm : 0m);''',
    'scenario PA destination cost',
)

methods_anchor = '''    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();'''
methods = '''    private static async Task<IResult> GetPricingLinesAsync(
        Guid id,
        ServiceDbContext db,
        CancellationToken ct)
    {
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);

        decimal destinationCostPerCbm;
        await using (var lookup = connection.CreateCommand())
        {
            lookup.CommandText = """
                SELECT carrier_destination_cost_total, maximum_cbm
                FROM pricing."OwnLclConsolidations"
                WHERE id=@id AND is_active=TRUE
                LIMIT 1;
                """;
            Add(lookup, "id", id);
            await using var reader = await lookup.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return Results.NotFound();
            destinationCostPerCbm = reader.GetDecimal(0) / Math.Max(0.01m, reader.GetDecimal(1));
        }

        var overrides = await LoadPricingLineOverridesAsync(connection, id, ct);
        var rows = OwnLclPricingLineCatalog.All.Select(definition =>
        {
            var fallbackCost = definition.DefaultCostUnit
                ?? (definition.LineKey == "PA_DESTINATION_CHARGE" ? destinationCostPerCbm : 0m);
            var value = ResolvePricingLine(overrides, definition.LineKey, fallbackCost);
            return new OwnLclPricingLineDto(
                definition.LineKey,
                definition.Scope,
                definition.Name,
                definition.ChargeBasis,
                value.Cost,
                value.Sale);
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

        var normalized = request.Rows
            .Select(row => row with { LineKey = Normalize(row.LineKey) })
            .ToArray();
        if (normalized.Select(row => row.LineKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length
            || normalized.Any(row => OwnLclPricingLineCatalog.Find(row.LineKey) is null || row.CostUnit < 0m || row.SaleUnit < 0m))
        {
            return Results.BadRequest(new { code = "Pricing.OwnLclPricingLineInvalid", message = "Las líneas del consolidado deben ser válidas y sus costos/ventas no pueden ser negativos." });
        }

        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);

        await using (var lookup = connection.CreateCommand())
        {
            lookup.CommandText = "SELECT 1 FROM pricing.\"OwnLclConsolidations\" WHERE id=@id AND is_active=TRUE LIMIT 1;";
            Add(lookup, "id", id);
            if (await lookup.ExecuteScalarAsync(ct) is null) return Results.NotFound();
        }

        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        foreach (var row in normalized)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = """
                INSERT INTO pricing."OwnLclConsolidationPricingLines"
                    (id, consolidation_id, line_key, cost_unit, sale_unit, updated_at_utc)
                VALUES
                    (gen_random_uuid(), @id, @key, @cost, @sale, now())
                ON CONFLICT (consolidation_id, line_key)
                DO UPDATE SET cost_unit=EXCLUDED.cost_unit, sale_unit=EXCLUDED.sale_unit, updated_at_utc=now();
                """;
            Add(command, "id", id);
            Add(command, "key", row.LineKey);
            Add(command, "cost", row.CostUnit);
            Add(command, "sale", row.SaleUnit);
            await command.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<Dictionary<string, (decimal Cost, decimal Sale)>> LoadPricingLineOverridesAsync(
        DbConnection connection,
        Guid id,
        CancellationToken ct)
    {
        var result = new Dictionary<string, (decimal Cost, decimal Sale)>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT line_key, cost_unit, sale_unit
            FROM pricing."OwnLclConsolidationPricingLines"
            WHERE consolidation_id=@id;
            """;
        Add(command, "id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result[reader.GetString(0)] = (reader.GetDecimal(1), reader.GetDecimal(2));
        return result;
    }

    private static (decimal Cost, decimal Sale) ResolvePricingLine(
        IReadOnlyDictionary<string, (decimal Cost, decimal Sale)> overrides,
        string lineKey,
        decimal fallbackCost)
    {
        if (overrides.TryGetValue(lineKey, out var stored)) return stored;
        var definition = OwnLclPricingLineCatalog.Find(lineKey)
            ?? throw new InvalidOperationException($"Línea LCL propia desconocida: {lineKey}.");
        return (definition.DefaultCostUnit ?? fallbackCost, definition.DefaultSaleUnit);
    }

'''
rep(fob, methods_anchor, methods + methods_anchor, 'pricing line methods')
rep(
    fob,
    '''public sealed record SaveOwnLclCostOverridesRequest(
    decimal OceanFreight,
    decimal MaximumCbm,
    decimal CarrierDestinationCostTotal,
    decimal PanamaToCostaRicaCost,
    decimal BunkerCost,
    decimal CostaRicaTransferBaseCbm);''',
    '''public sealed record SaveOwnLclCostOverridesRequest(
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
    decimal SaleUnit);

public sealed record SaveOwnLclPricingLineRequest(string LineKey, decimal CostUnit, decimal SaleUnit);
public sealed record SaveOwnLclPricingLinesRequest(IReadOnlyCollection<SaveOwnLclPricingLineRequest> Rows);''',
    'pricing line DTOs',
)

route = 'src/Dhole.Pricing.Api/Endpoints/OwnLclRouteMatrixV2Endpoints.cs'
rep(
    route,
    '''        var lines = new List<OwnLclQuoteLine>();
        AddLine(lines, "Flete Internacional Marítimo LCL", "CBM", billableCbm, freightCostPerCbm, freightSalePerCbm);
        AddDestinationLines(
            lines,
            destination,
            billableCbm,
            routeDestinationCostPerCbm);
        AddOriginLines(
            lines,
            incoterm,
            billableCbm,
            Math.Max(1, request.Sets),
            Math.Max(1, request.Hbl),
            request.PickupCost,
            request.PickupSale);''',
    '''        var pricingLineOverrides = await LoadPricingLineOverridesAsync(consolidation.Id, db, ct);
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
            billableCbm,
            Math.Max(1, request.Sets),
            Math.Max(1, request.Hbl),
            request.PickupCost,
            request.PickupSale,
            pricingLineOverrides);''',
    'quote pricing line load',
)

old_lines = '''    private static void AddDestinationLines(
        List<OwnLclQuoteLine> lines,
        string destination,
        decimal cbm,
        decimal routeDestinationCostPerCbm)
    {
        if (destination == "PA")
        {
            AddLine(lines, "Destination Charge", "CBM", cbm, routeDestinationCostPerCbm, 20m);
            AddLine(lines, "DMCE", "HBL", 1, 65m, 65m);
            AddLine(lines, "Handling", "HBL", 1, 25m, 25m);
            AddLine(lines, "Zone Charge", "HBL", 1, 30m, 30m);
            return;
        }

        if (destination == "CR")
        {
            AddLine(lines, "Manejos", "HBL", 1, 65m, 65m);
            AddLine(lines, "Zone Charge", "HBL", 1, 50m, 50m);
            return;
        }

        // The variable Centroamérica costs are already part of routeCostPerCbm.
        // These are the flat FOB destination-sale lines shown in the supplied sheets.
        AddLine(lines, "Documentación", "HBL", 1, 0m, 65m);
        AddLine(lines, "Zone Charge", "HBL", 1, 0m, 65m);
        AddLine(lines, "Manejos destino", "HBL", 1, 0m, 50m);
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

        // FCA/EXW origin handling from the CNCA sheets. EXW additionally gets pickup.
        AddLine(lines, "CFS", "CBM", cbm, 8m, 8m);
        AddLine(lines, "WHSE FEE", "CBM", cbm, 12m, 12m);
        AddLine(lines, "CUSTOMS", "SET", sets, 15m, 25m);
        AddLine(lines, "DOC FEE", "HBL", hbl, 15m, 65m);
        AddLine(lines, "VGM", "HBL", hbl, 0m, 25m);
        AddLine(lines, "MANIFEST", "HBL", hbl, 15m, 25m);

        if (incoterm == "EXW")
            AddLine(lines, "Recolecta", "Flat", 1, Math.Max(0m, pickupCost), Math.Max(0m, pickupSale));
    }
'''
new_lines = '''    private static void AddDestinationLines(
        List<OwnLclQuoteLine> lines,
        string destination,
        decimal cbm,
        decimal routeDestinationCostPerCbm,
        IReadOnlyDictionary<string, (decimal Cost, decimal Sale)> pricingLines)
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

        AddConfiguredLine(lines, pricingLines, "CA_DOCUMENTATION", 1);
        AddConfiguredLine(lines, pricingLines, "CA_ZONE", 1);
        AddConfiguredLine(lines, pricingLines, "CA_HANDLING", 1);
    }

    private static void AddOriginLines(
        List<OwnLclQuoteLine> lines,
        string incoterm,
        decimal cbm,
        int sets,
        int hbl,
        decimal pickupCost,
        decimal pickupSale,
        IReadOnlyDictionary<string, (decimal Cost, decimal Sale)> pricingLines)
    {
        if (incoterm == "FOB") return;

        AddConfiguredLine(lines, pricingLines, "ORIGIN_CFS", cbm);
        AddConfiguredLine(lines, pricingLines, "ORIGIN_WHSE", cbm);
        AddConfiguredLine(lines, pricingLines, "ORIGIN_CUSTOMS", sets);
        AddConfiguredLine(lines, pricingLines, "ORIGIN_DOC", hbl);
        AddConfiguredLine(lines, pricingLines, "ORIGIN_VGM", hbl);
        AddConfiguredLine(lines, pricingLines, "ORIGIN_MANIFEST", hbl);

        if (incoterm == "EXW")
            AddLine(lines, "Recolecta", "Flat", 1, Math.Max(0m, pickupCost), Math.Max(0m, pickupSale));
    }

    private static void AddConfiguredLine(
        List<OwnLclQuoteLine> lines,
        IReadOnlyDictionary<string, (decimal Cost, decimal Sale)> pricingLines,
        string lineKey,
        decimal quantity,
        decimal? fallbackCost = null)
    {
        var definition = OwnLclPricingLineCatalog.Find(lineKey)
            ?? throw new InvalidOperationException($"Línea LCL propia desconocida: {lineKey}.");
        var hasStored = pricingLines.TryGetValue(lineKey, out var stored);
        var cost = hasStored ? stored.Cost : definition.DefaultCostUnit ?? fallbackCost ?? 0m;
        var sale = hasStored ? stored.Sale : definition.DefaultSaleUnit;
        AddLine(lines, definition.Name, definition.ChargeBasis, quantity, cost, sale);
    }
'''
rep(route, old_lines, new_lines, 'configured quote lines')

route_helper_anchor = '''    private static async Task<decimal?> LoadHistoricalSaleAsync('''
route_helper = '''    private static async Task<Dictionary<string, (decimal Cost, decimal Sale)>> LoadPricingLineOverridesAsync(
        Guid consolidationId,
        ServiceDbContext db,
        CancellationToken ct)
    {
        var result = new Dictionary<string, (decimal Cost, decimal Sale)>(StringComparer.OrdinalIgnoreCase);
        var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT line_key, cost_unit, sale_unit
            FROM pricing."OwnLclConsolidationPricingLines"
            WHERE consolidation_id=@id;
            """;
        Add(command, "id", consolidationId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result[reader.GetString(0)] = (reader.GetDecimal(1), reader.GetDecimal(2));
        return result;
    }

'''
rep(route, route_helper_anchor, route_helper + route_helper_anchor, 'quote pricing override loader')

report = 'src/Dhole.Pricing.Infrastructure/Reports/RateReportDataFactory.cs'
rep(
    report,
    '''        var originOfficeQrDataUri = CreateQrDataUri(originOfficePublicUrl);

        var containers =''',
    '''        var originOfficeQrDataUri = CreateQrDataUri(originOfficePublicUrl);
        var showCarrier = rate.ShipmentMode != ShipmentMode.Lcl;

        var containers =''',
    'LCL report carrier flag',
)
rep(
    report,
    '''                agent = Text(rate.AgentName, "No asignado"),
                carrier = Text(rate.CarrierName, "No asignada"),
                pol = rate.PolName,''',
    '''                agent = Text(rate.AgentName, "No asignado"),
                carrier = showCarrier ? Text(rate.CarrierName, "No asignada") : string.Empty,
                showCarrier,
                pol = rate.PolName,''',
    'report carrier suppression',
)
