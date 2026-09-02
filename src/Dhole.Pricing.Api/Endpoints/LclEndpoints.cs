using System.Data;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class LclEndpoints
{
    private const decimal DefaultKgPerCbm = 500m;
    private const decimal DefaultCostaRicaLandFreight = 2140m;
    private const decimal DefaultBunker = 280m;
    private const decimal DefaultTruckCapacityCbm = 95m;

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>> ChinaRates =
        new Dictionary<string, IReadOnlyDictionary<string, decimal>>(StringComparer.OrdinalIgnoreCase)
        {
            ["San José, Costa Rica"] = ChinaRow(208m, 260m, 260m, 265m, 265m, 265m, 265m, 265m, 270m, 270m, 270m, 270m),
            ["CFZ Panama"] = ChinaRow(162m, 214m, 214m, 219m, 219m, 219m, 219m, 219m, 224m, 224m, 224m, 224m),
            ["Managua, Nicaragua"] = ChinaRow(162m, 214m, 214m, 219m, 219m, 219m, 219m, 219m, 224m, 224m, 224m, 224m),
            ["San Pedro Sula, Honduras"] = ChinaRow(162m, 214m, 214m, 219m, 219m, 219m, 219m, 219m, 224m, 224m, 224m, 224m),
            ["Ciudad de Guatemala, Guatemala"] = ChinaRow(162m, 214m, 214m, 219m, 219m, 219m, 219m, 219m, 224m, 224m, 224m, 224m),
            ["San Salvador, El Salvador"] = ChinaRow(162m, 214m, 214m, 219m, 219m, 219m, 219m, 219m, 224m, 224m, 224m, 224m),
        };

    public static IEndpointRouteBuilder MapLclEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/pricing/lcl").RequireAuthorization();

        group.MapGet("/rate-sources", ListRateSourcesAsync);
        group.MapPost("/own-consolidations", CreateOwnConsolidationAsync);
        group.MapPost("/coloader-rates", CreateColoaderRateAsync);
        group.MapPost("/coloader-rates/{id:guid}/approve", ApproveColoaderRateAsync);
        group.MapGet("/route-rules", GetRouteRules);
        group.MapPost("/calculate-cargo", CalculateCargo);

        return endpoints;
    }

    private static async Task<IResult> CreateOwnConsolidationAsync(
        CreateOwnLclRequest request,
        ServiceDbContext db,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BookingNumber))
            return Results.BadRequest(new { message = "El número de booking es requerido." });
        if (request.MaxCbm <= 0m)
            return Results.BadRequest(new { message = "La capacidad máxima de CBM debe ser mayor que cero." });
        if (request.OceanFreightAmount < 0m)
            return Results.BadRequest(new { message = "El flete marítimo no puede ser negativo." });

        var destinationCosts = await db.Costs
            .AsNoTracking()
            .Where(x =>
                x.IsActive
                && x.CarrierId == request.CarrierId
                && x.CurrencyId == request.CurrencyId
                && (!x.ShipmentMode.HasValue || x.ShipmentMode == ShipmentMode.Lcl)
                && (x.PoeId == request.PoeId
                    || (x.PortId == request.PoeId
                        && (x.PortRole == CostPortRole.Poe || x.PortRole == CostPortRole.Any))))
            .ToListAsync(cancellationToken);

        var destinationFixedTotal = 0m;
        var destinationPerCbm = 0m;
        var breakdown = new List<LclDestinationCostBreakdown>();

        foreach (var cost in destinationCosts)
        {
            var isPerCbm = cost.ChargeBasis is ChargeBasis.PerCbm or ChargeBasis.PerChargeableCbm;
            var perCbm = isPerCbm ? cost.CostAmount : cost.CostAmount / request.MaxCbm;
            if (isPerCbm)
                destinationPerCbm += cost.CostAmount;
            else
                destinationFixedTotal += cost.CostAmount;

            breakdown.Add(new LclDestinationCostBreakdown(
                cost.Id,
                cost.Name,
                cost.ChargeBasis.ToString(),
                cost.CostAmount,
                decimal.Round(perCbm, 4)));
        }

        var oceanPerCbm = request.OceanFreightAmount / request.MaxCbm;
        var destinationProratedPerCbm = destinationFixedTotal / request.MaxCbm;
        var baseRatePerCbm = oceanPerCbm + destinationProratedPerCbm + destinationPerCbm;
        var destinationTotalAtCapacity = destinationFixedTotal + (destinationPerCbm * request.MaxCbm);
        var id = Guid.NewGuid();

        await ExecuteAsync(db, """
            INSERT INTO pricing."LclRateSources" (
                "Id", "SourceType", "BookingNumber", "Etd", "CarrierId", "CarrierName", "CarrierCode",
                "PolId", "PolName", "PolCode", "PoeId", "PoeName", "PoeCode",
                "ContainerTypeId", "ContainerTypeName", "ContainerTypeCode", "MaxCbm",
                "OceanFreightAmount", "DestinationCostTotal", "OceanFreightPerCbm", "DestinationCostPerCbm",
                "BaseRatePerCbm", "CurrencyId", "CurrencyName", "CurrencyCode", "ApprovalStatus",
                "DefaultLandFreightAmount", "DefaultBunkerAmount", "TruckCapacityCbm", "IsActive", "CreatedAtUtc")
            VALUES (
                @id, 'Own', @booking, @etd, @carrierId, @carrierName, @carrierCode,
                @polId, @polName, @polCode, @poeId, @poeName, @poeCode,
                @containerTypeId, @containerTypeName, @containerTypeCode, @maxCbm,
                @oceanFreight, @destinationTotal, @oceanPerCbm, @destinationPerCbm,
                @baseRatePerCbm, @currencyId, @currencyName, @currencyCode, 'Approved',
                @landFreight, @bunker, @truckCapacity, TRUE, NOW())
            """,
            cancellationToken,
            ("id", id), ("booking", request.BookingNumber.Trim()), ("etd", request.Etd),
            ("carrierId", request.CarrierId), ("carrierName", request.CarrierName), ("carrierCode", request.CarrierCode),
            ("polId", request.PolId), ("polName", request.PolName), ("polCode", request.PolCode),
            ("poeId", request.PoeId), ("poeName", request.PoeName), ("poeCode", request.PoeCode),
            ("containerTypeId", request.ContainerTypeId), ("containerTypeName", request.ContainerTypeName),
            ("containerTypeCode", request.ContainerTypeCode), ("maxCbm", request.MaxCbm),
            ("oceanFreight", request.OceanFreightAmount), ("destinationTotal", destinationTotalAtCapacity),
            ("oceanPerCbm", oceanPerCbm), ("destinationPerCbm", destinationProratedPerCbm + destinationPerCbm),
            ("baseRatePerCbm", baseRatePerCbm), ("currencyId", request.CurrencyId),
            ("currencyName", request.CurrencyName), ("currencyCode", request.CurrencyCode),
            ("landFreight", request.DefaultLandFreightAmount ?? DefaultCostaRicaLandFreight),
            ("bunker", request.DefaultBunkerAmount ?? DefaultBunker),
            ("truckCapacity", request.TruckCapacityCbm ?? DefaultTruckCapacityCbm));

        return Results.Ok(new
        {
            id,
            sourceType = "Own",
            approvalStatus = "Approved",
            oceanFreightPerCbm = decimal.Round(oceanPerCbm, 4),
            destinationFixedTotal = decimal.Round(destinationFixedTotal, 2),
            destinationVariablePerCbm = decimal.Round(destinationPerCbm, 4),
            destinationCostPerCbm = decimal.Round(destinationProratedPerCbm + destinationPerCbm, 4),
            baseRatePerCbm = decimal.Round(baseRatePerCbm, 4),
            destinationCosts = breakdown,
        });
    }

    private static async Task<IResult> CreateColoaderRateAsync(
        CreateColoaderLclRequest request,
        ServiceDbContext db,
        CancellationToken cancellationToken)
    {
        if (request.RatePerCbm <= 0m)
            return Results.BadRequest(new { message = "La tarifa del coloader por CBM debe ser mayor que cero." });

        var id = Guid.NewGuid();
        await ExecuteAsync(db, """
            INSERT INTO pricing."LclRateSources" (
                "Id", "SourceType", "ProviderId", "ProviderName", "ProviderCode", "CarrierId", "CarrierName", "CarrierCode",
                "PolId", "PolName", "PolCode", "PoeId", "PoeName", "PoeCode", "MaxCbm", "BaseRatePerCbm",
                "CurrencyId", "CurrencyName", "CurrencyCode", "ApprovalStatus", "ValidFrom", "ValidTo",
                "DefaultLandFreightAmount", "DefaultBunkerAmount", "TruckCapacityCbm", "Notes", "IsActive", "CreatedAtUtc")
            VALUES (
                @id, 'Coloader', @providerId, @providerName, @providerCode, @carrierId, @carrierName, @carrierCode,
                @polId, @polName, @polCode, @poeId, @poeName, @poeCode, @maxCbm, @ratePerCbm,
                @currencyId, @currencyName, @currencyCode, 'PendingApproval', @validFrom, @validTo,
                @landFreight, @bunker, @truckCapacity, @notes, TRUE, NOW())
            """,
            cancellationToken,
            ("id", id), ("providerId", request.ProviderId), ("providerName", request.ProviderName), ("providerCode", request.ProviderCode),
            ("carrierId", request.CarrierId), ("carrierName", request.CarrierName), ("carrierCode", request.CarrierCode),
            ("polId", request.PolId), ("polName", request.PolName), ("polCode", request.PolCode),
            ("poeId", request.PoeId), ("poeName", request.PoeName), ("poeCode", request.PoeCode),
            ("maxCbm", request.MaxCbm), ("ratePerCbm", request.RatePerCbm),
            ("currencyId", request.CurrencyId), ("currencyName", request.CurrencyName), ("currencyCode", request.CurrencyCode),
            ("validFrom", request.ValidFrom), ("validTo", request.ValidTo),
            ("landFreight", request.DefaultLandFreightAmount ?? DefaultCostaRicaLandFreight),
            ("bunker", request.DefaultBunkerAmount ?? DefaultBunker),
            ("truckCapacity", request.TruckCapacityCbm ?? DefaultTruckCapacityCbm), ("notes", request.Notes));

        return Results.Ok(new { id, sourceType = "Coloader", approvalStatus = "PendingApproval" });
    }

    private static async Task<IResult> ApproveColoaderRateAsync(
        Guid id,
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var affected = await ExecuteAsync(db, """
            UPDATE pricing."LclRateSources"
            SET "ApprovalStatus" = 'Approved', "ApprovedAtUtc" = NOW(), "ApprovedBy" = @approvedBy
            WHERE "Id" = @id AND "SourceType" = 'Coloader' AND "IsActive" = TRUE
            """,
            cancellationToken,
            ("id", id), ("approvedBy", httpContext.User.Identity?.Name ?? "system"));

        return affected == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> ListRateSourcesAsync(
        ServiceDbContext db,
        string? sourceType,
        bool approvedOnly = true,
        CancellationToken cancellationToken = default)
    {
        var sql = """
            SELECT "Id", "SourceType", "BookingNumber", "Etd", "ProviderName", "CarrierId", "CarrierName", "CarrierCode",
                   "PolId", "PolName", "PolCode", "PoeId", "PoeName", "PoeCode", "ContainerTypeId", "ContainerTypeName",
                   "ContainerTypeCode", "MaxCbm", "OceanFreightAmount", "DestinationCostTotal", "BaseRatePerCbm",
                   "CurrencyId", "CurrencyName", "CurrencyCode", "ApprovalStatus", "ValidFrom", "ValidTo",
                   "DefaultLandFreightAmount", "DefaultBunkerAmount", "TruckCapacityCbm", "Notes"
            FROM pricing."LclRateSources"
            WHERE "IsActive" = TRUE
              AND (@sourceType IS NULL OR "SourceType" = @sourceType)
              AND (@approvedOnly = FALSE OR "ApprovalStatus" = 'Approved')
            ORDER BY COALESCE("Etd", "ValidFrom", "CreatedAtUtc") DESC
            """;

        var rows = new List<LclRateSourceDto>();
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddParameter(command, "sourceType", sourceType);
            AddParameter(command, "approvedOnly", approvedOnly);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new LclRateSourceDto(
                    reader.GetGuid(0), reader.GetString(1), GetNullableString(reader, 2), GetNullableDateTime(reader, 3),
                    GetNullableString(reader, 4), reader.GetGuid(5), reader.GetString(6), reader.GetString(7),
                    reader.GetGuid(8), reader.GetString(9), reader.GetString(10), reader.GetGuid(11), reader.GetString(12), reader.GetString(13),
                    GetNullableGuid(reader, 14), GetNullableString(reader, 15), GetNullableString(reader, 16), GetNullableDecimal(reader, 17),
                    GetNullableDecimal(reader, 18), GetNullableDecimal(reader, 19), reader.GetDecimal(20), reader.GetGuid(21), reader.GetString(22), reader.GetString(23),
                    reader.GetString(24), GetNullableDateTime(reader, 25), GetNullableDateTime(reader, 26), reader.GetDecimal(27), reader.GetDecimal(28), reader.GetDecimal(29),
                    GetNullableString(reader, 30)));
            }
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }

        return Results.Ok(rows);
    }

    private static IResult GetRouteRules()
    {
        var destinations = ChinaRates.Select(destination => new
        {
            destination = destination.Key,
            rates = destination.Value,
        });

        return Results.Ok(new
        {
            kgPerCbm = DefaultKgPerCbm,
            minimumChargeableCbm = 1m,
            ownChinaBasePol = "Shanghai, China",
            ownChinaBasePoe = "Balboa, Panama",
            costaRica = new
            {
                landFreightAmount = DefaultCostaRicaLandFreight,
                bunkerAmount = DefaultBunker,
                truckCapacityCbm = DefaultTruckCapacityCbm,
                landAndBunkerPerCbm = decimal.Round((DefaultCostaRicaLandFreight + DefaultBunker) / DefaultTruckCapacityCbm, 4),
            },
            destinations,
            destinationRules = new Dictionary<string, object>
            {
                ["CFZ Panama"] = new { destinationPerCbm = 20m, destinationMin = 20m, dmce = 65m, handling = 25m, zone = 30m },
                ["San José, Costa Rica"] = new { handling = 65m, zone = 50m },
                ["Managua, Nicaragua"] = new { transshipment = 29m, inland = 40m, stuffing = 10m, docs = 140m, handling = 45m, destinationHandling = 70m },
                ["San Pedro Sula, Honduras"] = new { transshipment = 29m, inland = 50m, stuffing = 10m, docs = 140m, handling = 45m, destinationHandling = 70m },
                ["Ciudad de Guatemala, Guatemala"] = new { transshipment = 29m, inland = 48m, stuffing = 10m, docs = 140m, handling = 45m, destinationHandling = 70m },
                ["San Salvador, El Salvador"] = new { transshipment = 29m, inland = 40m, stuffing = 10m, docs = 140m, handling = 45m, destinationHandling = 70m },
            },
            originRules = new
            {
                fcaAndExw = new { cfsPerCbm = 8m, customsPerSet = 25m, docFeePerHbl = 65m, vgmPerHbl = 25m, manifestPerHbl = 25m },
                pickupOnlyFor = "EXW",
            },
            source = "CNCA-023/#048",
        });
    }

    private static IResult CalculateCargo(CalculateLclCargoRequest request)
    {
        var lines = request.Lines ?? [];
        var kgPerCbm = request.KgPerCbm > 0m ? request.KgPerCbm : DefaultKgPerCbm;
        var result = lines.Select((line, index) =>
        {
            var units = Math.Max(0, line.Pallets > 0 ? line.Pallets : line.Units);
            var dimensional = (Math.Max(0m, line.LengthCm) * Math.Max(0m, line.WidthCm) * Math.Max(0m, line.HeightCm) * units) / 1_000_000m;
            var weightCbm = Math.Max(0m, line.WeightKg) / kgPerCbm;
            var chargeable = Math.Max(dimensional, weightCbm);
            return new LclCargoResult(index + 1, dimensional, weightCbm, chargeable);
        }).ToList();

        var dimensionalTotal = result.Sum(x => x.DimensionalCbm);
        var weightCbmTotal = result.Sum(x => x.WeightCbm);
        var chargeableTotal = result.Sum(x => x.ChargeableCbm);
        var freightChargeableCbm = chargeableTotal > 0m ? Math.Max(1m, chargeableTotal) : 0m;

        return Results.Ok(new
        {
            lines = result,
            dimensionalCbm = decimal.Round(dimensionalTotal, 3),
            weightCbm = decimal.Round(weightCbmTotal, 3),
            chargeableCbm = decimal.Round(chargeableTotal, 3),
            freightChargeableCbm = decimal.Round(freightChargeableCbm, 3),
            kgPerCbm,
        });
    }

    private static IReadOnlyDictionary<string, decimal> ChinaRow(params decimal[] values)
    {
        string[] ports = ["SHANGHAI", "NINGBO", "QINGDAO", "XIAMEN", "SHANTOU", "DALIAN", "CHONGQING", "FUZHOU", "SHENZHEN", "XINGANG", "SHEKOU", "GUANGZHOU"];
        return ports.Zip(values, (port, value) => new KeyValuePair<string, decimal>(port, value))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<int> ExecuteAsync(
        ServiceDbContext db,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var parameter in parameters) AddParameter(command, parameter.Name, parameter.Value);
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = $"@{name}";
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string? GetNullableString(System.Data.Common.DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static Guid? GetNullableGuid(System.Data.Common.DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    private static decimal? GetNullableDecimal(System.Data.Common.DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    private static DateTime? GetNullableDateTime(System.Data.Common.DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);

    public sealed record CreateOwnLclRequest(
        string BookingNumber,
        DateTime Etd,
        Guid CarrierId,
        string CarrierName,
        string CarrierCode,
        Guid PolId,
        string PolName,
        string PolCode,
        Guid PoeId,
        string PoeName,
        string PoeCode,
        Guid? ContainerTypeId,
        string? ContainerTypeName,
        string? ContainerTypeCode,
        decimal MaxCbm,
        decimal OceanFreightAmount,
        Guid CurrencyId,
        string CurrencyName,
        string CurrencyCode,
        decimal? DefaultLandFreightAmount = null,
        decimal? DefaultBunkerAmount = null,
        decimal? TruckCapacityCbm = null);

    public sealed record CreateColoaderLclRequest(
        Guid? ProviderId,
        string? ProviderName,
        string? ProviderCode,
        Guid CarrierId,
        string CarrierName,
        string CarrierCode,
        Guid PolId,
        string PolName,
        string PolCode,
        Guid PoeId,
        string PoeName,
        string PoeCode,
        decimal? MaxCbm,
        decimal RatePerCbm,
        Guid CurrencyId,
        string CurrencyName,
        string CurrencyCode,
        DateTime? ValidFrom,
        DateTime? ValidTo,
        string? Notes,
        decimal? DefaultLandFreightAmount = null,
        decimal? DefaultBunkerAmount = null,
        decimal? TruckCapacityCbm = null);

    public sealed record CalculateLclCargoRequest(decimal KgPerCbm, IReadOnlyCollection<LclCargoLine>? Lines);
    public sealed record LclCargoLine(int Units, int Pallets, decimal WeightKg, decimal LengthCm, decimal WidthCm, decimal HeightCm);
    public sealed record LclCargoResult(int Line, decimal DimensionalCbm, decimal WeightCbm, decimal ChargeableCbm);
    public sealed record LclDestinationCostBreakdown(Guid CostId, string Name, string ChargeBasis, decimal Amount, decimal PerCbm);

    public sealed record LclRateSourceDto(
        Guid Id,
        string SourceType,
        string? BookingNumber,
        DateTime? Etd,
        string? ProviderName,
        Guid CarrierId,
        string CarrierName,
        string CarrierCode,
        Guid PolId,
        string PolName,
        string PolCode,
        Guid PoeId,
        string PoeName,
        string PoeCode,
        Guid? ContainerTypeId,
        string? ContainerTypeName,
        string? ContainerTypeCode,
        decimal? MaxCbm,
        decimal? OceanFreightAmount,
        decimal? DestinationCostTotal,
        decimal BaseRatePerCbm,
        Guid CurrencyId,
        string CurrencyName,
        string CurrencyCode,
        string ApprovalStatus,
        DateTime? ValidFrom,
        DateTime? ValidTo,
        decimal DefaultLandFreightAmount,
        decimal DefaultBunkerAmount,
        decimal TruckCapacityCbm,
        string? Notes);
}
