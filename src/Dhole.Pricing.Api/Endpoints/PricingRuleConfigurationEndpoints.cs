using System.Data;
using System.Data.Common;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Contracts.Rates.Request;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class PricingRuleConfigurationEndpoints
{
    public static IEndpointRouteBuilder MapPricingRuleConfigurationEndpoints(this IEndpointRouteBuilder app)
    {
        var freeDays = app.MapGroup("/api/pricing/carrier-free-day-rules").WithTags("Pricing rules").RequireAuthorization();
        freeDays.MapGet("/", BrowseCarrierFreeDaysAsync).RequireScope(PricingConstants.Scopes.RateTermView);
        freeDays.MapGet("/resolve/{carrierId:guid}", ResolveCarrierFreeDaysAsync).RequireScope(PricingConstants.Scopes.RateTermSelect);
        freeDays.MapPost("/", CreateCarrierFreeDaysAsync).RequireScope(PricingConstants.Scopes.RateTermCreate);
        freeDays.MapPut("/{id:guid}", UpdateCarrierFreeDaysAsync).RequireScope(PricingConstants.Scopes.RateTermUpdate);
        freeDays.MapDelete("/{id:guid}", DeleteCarrierFreeDaysAsync).RequireScope(PricingConstants.Scopes.RateTermDelete);

        var blocks = app.MapGroup("/api/pricing/rate-term-blocks").WithTags("Pricing rules").RequireAuthorization();
        blocks.MapGet("/", BrowseBlocksAsync).RequireScope(PricingConstants.Scopes.RateTermView);
        blocks.MapGet("/resolve", ResolveBlocksAsync).RequireScope(PricingConstants.Scopes.RateTermSelect);
        blocks.MapPost("/", CreateBlockAsync).RequireScope(PricingConstants.Scopes.RateTermCreate);
        blocks.MapPut("/{id:guid}", UpdateBlockAsync).RequireScope(PricingConstants.Scopes.RateTermUpdate);
        blocks.MapDelete("/{id:guid}", DeleteBlockAsync).RequireScope(PricingConstants.Scopes.RateTermDelete);
        return app;
    }

    private static async Task<IResult> BrowseCarrierFreeDaysAsync(ServiceDbContext db, CancellationToken ct)
    {
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, carrier_id, carrier_name, carrier_code, free_days, is_active
            FROM pricing."CarrierFreeDayRules"
            ORDER BY carrier_name, carrier_code;
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<CarrierFreeDayRuleDto>();
        while (await reader.ReadAsync(ct)) rows.Add(ReadFreeDayRule(reader));
        return Results.Ok(rows);
    }

    private static async Task<IResult> ResolveCarrierFreeDaysAsync(Guid carrierId, ServiceDbContext db, CancellationToken ct)
    {
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, carrier_id, carrier_name, carrier_code, free_days, is_active
            FROM pricing."CarrierFreeDayRules"
            WHERE carrier_id = @carrier_id AND is_active = TRUE
            LIMIT 1;
            """;
        Add(command, "carrier_id", carrierId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return Results.Ok((CarrierFreeDayRuleDto?)null);
        return Results.Ok(ReadFreeDayRule(reader));
    }

    private static async Task<IResult> CreateCarrierFreeDaysAsync(CreateCarrierFreeDayRuleRequest request, ServiceDbContext db, CancellationToken ct)
    {
        var validation = ValidateFreeDayRequest(request.CarrierId, request.CarrierName, request.CarrierCode, request.FreeDays);
        if (validation is not null) return Results.BadRequest(validation);
        var id = Guid.NewGuid();
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO pricing."CarrierFreeDayRules"
              (id, carrier_id, carrier_name, carrier_code, free_days, is_active, created_at_utc)
            VALUES (@id, @carrier_id, @carrier_name, @carrier_code, @free_days, @is_active, now())
            ON CONFLICT (carrier_id) DO NOTHING;
            """;
        Add(command, "id", id); Add(command, "carrier_id", request.CarrierId); Add(command, "carrier_name", request.CarrierName.Trim());
        Add(command, "carrier_code", request.CarrierCode.Trim()); Add(command, "free_days", request.FreeDays); Add(command, "is_active", request.IsActive);
        var affected = await command.ExecuteNonQueryAsync(ct);
        return affected == 0
            ? Results.Conflict(new { code = "Pricing.CarrierFreeDaysAlreadyExists", message = "La naviera ya tiene una configuración de días libres." })
            : Results.Ok(id);
    }

    private static async Task<IResult> UpdateCarrierFreeDaysAsync(Guid id, UpdateCarrierFreeDayRuleRequest request, ServiceDbContext db, CancellationToken ct)
    {
        var validation = ValidateFreeDayRequest(request.CarrierId, request.CarrierName, request.CarrierCode, request.FreeDays);
        if (validation is not null) return Results.BadRequest(validation);
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE pricing."CarrierFreeDayRules"
            SET carrier_id=@carrier_id, carrier_name=@carrier_name, carrier_code=@carrier_code,
                free_days=@free_days, is_active=@is_active, updated_at_utc=now()
            WHERE id=@id;
            """;
        Add(command, "id", id); Add(command, "carrier_id", request.CarrierId); Add(command, "carrier_name", request.CarrierName.Trim());
        Add(command, "carrier_code", request.CarrierCode.Trim()); Add(command, "free_days", request.FreeDays); Add(command, "is_active", request.IsActive);
        try
        {
            return await command.ExecuteNonQueryAsync(ct) == 0 ? Results.NotFound() : Results.NoContent();
        }
        catch (DbException)
        {
            return Results.Conflict(new { code = "Pricing.CarrierFreeDaysAlreadyExists", message = "La naviera ya tiene una configuración de días libres." });
        }
    }

    private static async Task<IResult> DeleteCarrierFreeDaysAsync(Guid id, ServiceDbContext db, CancellationToken ct)
    {
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM pricing.\"CarrierFreeDayRules\" WHERE id=@id;";
        Add(command, "id", id);
        return await command.ExecuteNonQueryAsync(ct) == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> BrowseBlocksAsync(ServiceDbContext db, CancellationToken ct)
        => Results.Ok(await LoadBlocksAsync(db, null, null, null, null, includeInactive: true, ct));

    private static async Task<IResult> ResolveBlocksAsync(string? rateType, string? shipmentMode, Guid? poeId, Guid? incotermId, ServiceDbContext db, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(rateType) && !Enum.TryParse<RateType>(rateType, true, out _))
            return Results.BadRequest(new { code = "Pricing.InvalidRateType", message = "El tipo de tarifa indicado no es válido." });
        if (!string.IsNullOrWhiteSpace(shipmentMode) && !Enum.TryParse<ShipmentMode>(shipmentMode, true, out _))
            return Results.BadRequest(new { code = "Pricing.InvalidShipmentMode", message = "La modalidad indicada no es válida." });
        return Results.Ok(await LoadBlocksAsync(db, rateType, shipmentMode, poeId, incotermId, includeInactive: false, ct));
    }

    private static async Task<IResult> CreateBlockAsync(UpsertRateTermBlockRequest request, ServiceDbContext db, CancellationToken ct)
    {
        var validation = ValidateBlock(request);
        if (validation is not null) return Results.BadRequest(validation);
        var id = Guid.NewGuid();
        await SaveBlockAsync(db, id, request, isInsert: true, ct);
        return Results.Ok(id);
    }

    private static async Task<IResult> UpdateBlockAsync(Guid id, UpsertRateTermBlockRequest request, ServiceDbContext db, CancellationToken ct)
    {
        var validation = ValidateBlock(request);
        if (validation is not null) return Results.BadRequest(validation);
        var affected = await SaveBlockAsync(db, id, request, isInsert: false, ct);
        return affected ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> DeleteBlockAsync(Guid id, ServiceDbContext db, CancellationToken ct)
    {
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM pricing.\"RateTermBlocks\" WHERE id=@id;";
        Add(command, "id", id);
        return await command.ExecuteNonQueryAsync(ct) == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static object? ValidateFreeDayRequest(Guid carrierId, string carrierName, string carrierCode, int freeDays)
    {
        if (carrierId == Guid.Empty || string.IsNullOrWhiteSpace(carrierName) || string.IsNullOrWhiteSpace(carrierCode))
            return new { code = "Pricing.CarrierRequired", message = "Seleccione una naviera válida." };
        if (freeDays < 0) return new { code = "Pricing.FreeDaysInvalid", message = "Los días libres no pueden ser negativos." };
        return null;
    }

    private static object? ValidateBlock(UpsertRateTermBlockRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return new { code = "Pricing.RateTermBlockNameRequired", message = "El nombre del bloque es requerido." };
        if (!string.IsNullOrWhiteSpace(request.RateType) && !Enum.TryParse<RateType>(request.RateType, true, out _))
            return new { code = "Pricing.InvalidRateType", message = "El tipo de tarifa indicado no es válido." };
        if (!string.IsNullOrWhiteSpace(request.ShipmentMode) && !Enum.TryParse<ShipmentMode>(request.ShipmentMode, true, out _))
            return new { code = "Pricing.InvalidShipmentMode", message = "La modalidad indicada no es válida." };
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Includes", "SubjectTo", "Excludes" };
        if (request.Items.Any(x => !categories.Contains(x.Category)))
            return new { code = "Pricing.InvalidRateTermCategory", message = "La categoría de uno de los ítems no es válida." };
        if (request.Items.GroupBy(x => x.RateTermItemId).Any(x => x.Count() > 1))
            return new { code = "Pricing.RateTermItemDuplicated", message = "Un ítem solo puede pertenecer a una categoría dentro del bloque." };
        return null;
    }

    private static async Task<bool> SaveBlockAsync(ServiceDbContext db, Guid id, UpsertRateTermBlockRequest request, bool isInsert, CancellationToken ct)
    {
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = isInsert
            ? """INSERT INTO pricing."RateTermBlocks" (id,name,rate_type,shipment_mode,poe_id,poe_name,poe_code,incoterm_id,incoterm_name,incoterm_code,sort_order,is_active,created_at_utc) VALUES (@id,@name,@rate_type,@shipment_mode,@poe_id,@poe_name,@poe_code,@incoterm_id,@incoterm_name,@incoterm_code,@sort_order,@is_active,now());"""
            : """UPDATE pricing."RateTermBlocks" SET name=@name,rate_type=@rate_type,shipment_mode=@shipment_mode,poe_id=@poe_id,poe_name=@poe_name,poe_code=@poe_code,incoterm_id=@incoterm_id,incoterm_name=@incoterm_name,incoterm_code=@incoterm_code,sort_order=@sort_order,is_active=@is_active,updated_at_utc=now() WHERE id=@id;""";
        Add(command,"id",id); Add(command,"name",request.Name.Trim()); Add(command,"rate_type",NullIfBlank(request.RateType)); Add(command,"shipment_mode",NullIfBlank(request.ShipmentMode));
        Add(command,"poe_id",request.PoeId); Add(command,"poe_name",NullIfBlank(request.PoeName)); Add(command,"poe_code",NullIfBlank(request.PoeCode));
        Add(command,"incoterm_id",request.IncotermId); Add(command,"incoterm_name",NullIfBlank(request.IncotermName)); Add(command,"incoterm_code",NullIfBlank(request.IncotermCode));
        Add(command,"sort_order",Math.Max(0,request.SortOrder)); Add(command,"is_active",request.IsActive);
        var affected = await command.ExecuteNonQueryAsync(ct);
        if (!isInsert && affected == 0) { await tx.RollbackAsync(ct); return false; }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = tx; delete.CommandText = "DELETE FROM pricing.\"RateTermBlockItems\" WHERE block_id=@block_id;"; Add(delete,"block_id",id); await delete.ExecuteNonQueryAsync(ct);
        }

        var normalizedItems = request.Items
            .Select(x => new
            {
                x.RateTermItemId,
                Category = NormalizeCategory(x.Category),
                SortOrder = Math.Max(0, x.SortOrder)
            })
            .OrderBy(x => x.Category, StringComparer.Ordinal)
            .ThenBy(x => x.SortOrder)
            .ToArray();

        foreach (var item in normalizedItems)
        {
            await using var itemCommand = connection.CreateCommand();
            itemCommand.Transaction = tx;
            itemCommand.CommandText = "INSERT INTO pricing.\"RateTermBlockItems\" (block_id,rate_term_item_id,category,sort_order) VALUES (@block_id,@item_id,@category,@sort_order);";
            Add(itemCommand,"block_id",id); Add(itemCommand,"item_id",item.RateTermItemId); Add(itemCommand,"category",item.Category); Add(itemCommand,"sort_order",item.SortOrder);
            await itemCommand.ExecuteNonQueryAsync(ct);
        }

        // Verificación defensiva: las tres categorías, incluida Excludes, deben quedar
        // persistidas antes de confirmar la transacción.
        await using (var verify = connection.CreateCommand())
        {
            verify.Transaction = tx;
            verify.CommandText = """
                SELECT category, COUNT(*)
                FROM pricing."RateTermBlockItems"
                WHERE block_id=@block_id
                GROUP BY category;
                """;
            Add(verify, "block_id", id);
            await using var reader = await verify.ExecuteReaderAsync(ct);
            var persistedCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(ct))
            {
                persistedCounts[NormalizeCategory(reader.GetString(0))] = Convert.ToInt32(reader.GetInt64(1));
            }

            foreach (var category in new[] { "Includes", "SubjectTo", "Excludes" })
            {
                var expected = normalizedItems.Count(x => x.Category == category);
                var persisted = persistedCounts.GetValueOrDefault(category);
                if (expected != persisted)
                    throw new InvalidOperationException($"No se persistieron correctamente los ítems de la categoría {category}.");
            }
        }

        await tx.CommitAsync(ct);
        return true;
    }

    private static async Task<IReadOnlyCollection<RateTermBlockDto>> LoadBlocksAsync(ServiceDbContext db, string? rateType, string? shipmentMode, Guid? poeId, Guid? incotermId, bool includeInactive, CancellationToken ct)
    {
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT b.id,b.name,b.rate_type,b.shipment_mode,b.poe_id,b.poe_name,b.poe_code,b.incoterm_id,b.incoterm_name,b.incoterm_code,b.sort_order,b.is_active,
                   i.rate_term_item_id,t.text,i.category,i.sort_order,t.is_active
            FROM pricing."RateTermBlocks" b
            LEFT JOIN pricing."RateTermBlockItems" i ON i.block_id=b.id
            LEFT JOIN pricing."RateTermItems" t ON t.id=i.rate_term_item_id
            WHERE (@include_inactive OR b.is_active=TRUE)
              AND (@rate_type IS NULL OR b.rate_type IS NULL OR lower(b.rate_type)=lower(@rate_type))
              AND (@shipment_mode IS NULL OR b.shipment_mode IS NULL OR lower(b.shipment_mode)=lower(@shipment_mode))
              AND (@poe_id IS NULL OR b.poe_id IS NULL OR b.poe_id=@poe_id)
              AND (@incoterm_id IS NULL OR b.incoterm_id IS NULL OR b.incoterm_id=@incoterm_id)
            ORDER BY b.sort_order,b.name,i.sort_order,t.text;
            """;
        Add(command, "include_inactive", includeInactive, DbType.Boolean);
        Add(command, "rate_type", NullIfBlank(rateType), DbType.String);
        Add(command, "shipment_mode", NullIfBlank(shipmentMode), DbType.String);
        Add(command, "poe_id", poeId, DbType.Guid);
        Add(command, "incoterm_id", incotermId, DbType.Guid);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var blocks = new Dictionary<Guid, MutableBlock>();
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            if (!blocks.TryGetValue(id, out var block))
            {
                block = new MutableBlock(id, reader.GetString(1), GetNullableString(reader,2), GetNullableString(reader,3), GetNullableGuid(reader,4), GetNullableString(reader,5), GetNullableString(reader,6), GetNullableGuid(reader,7), GetNullableString(reader,8), GetNullableString(reader,9), reader.GetInt32(10), reader.GetBoolean(11));
                blocks[id]=block;
            }
            if (!reader.IsDBNull(12) && !reader.IsDBNull(13))
            {
                var itemIsActive = reader.IsDBNull(16) || reader.GetBoolean(16);
                // En administración se devuelven también las asociaciones a ítems inactivos para
                // que editar/guardar un bloque no las borre silenciosamente. Al resolver un bloque
                // para una cotización solo se aplican ítems activos.
                if (includeInactive || itemIsActive)
                {
                    block.Items.Add(new RateTermBlockItemDto(
                        reader.GetGuid(12),
                        reader.GetString(13),
                        NormalizeCategory(reader.GetString(14)),
                        reader.GetInt32(15)));
                }
            }
        }
        return blocks.Values.Select(x => x.ToDto()).ToArray();
    }

    private sealed class MutableBlock(Guid id,string name,string? rateType,string? shipmentMode,Guid? poeId,string? poeName,string? poeCode,Guid? incotermId,string? incotermName,string? incotermCode,int sortOrder,bool isActive)
    {
        public List<RateTermBlockItemDto> Items { get; } = [];
        public RateTermBlockDto ToDto() => new(id,name,rateType,shipmentMode,poeId,poeName,poeCode,incotermId,incotermName,incotermCode,sortOrder,isActive,Items);
    }

    private static CarrierFreeDayRuleDto ReadFreeDayRule(DbDataReader reader) => new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.GetString(3),reader.GetInt32(4),reader.GetBoolean(5));
    private static string NormalizeCategory(string value) => value.Equals("Includes",StringComparison.OrdinalIgnoreCase) ? "Includes" : value.Equals("SubjectTo",StringComparison.OrdinalIgnoreCase) ? "SubjectTo" : "Excludes";
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? GetNullableString(DbDataReader r,int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static Guid? GetNullableGuid(DbDataReader r,int i) => r.IsDBNull(i) ? null : r.GetGuid(i);
    private static async Task EnsureOpenAsync(DbConnection connection,CancellationToken ct) { if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct); }
    private static void Add(DbCommand command, string name, object? value, DbType? dbType = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@" + name;
        if (dbType.HasValue) parameter.DbType = dbType.Value;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
