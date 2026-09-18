using System.Data.Common;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class FtlTariffEndpoints
{
    public static IEndpointRouteBuilder MapFtlTariffEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/pricing/ftl-tariffs")
            .WithTags("Land tariffs")
            .RequireAuthorization();

        group.MapGet("/", BrowseAsync).RequireScope(PricingConstants.Scopes.CostView);
        group.MapGet("/resolve", ResolveAsync).RequireScope(PricingConstants.Scopes.CostSelect);
        group.MapPost("/", CreateAsync).RequireScope(PricingConstants.Scopes.CostUpdate);
        group.MapPost("/import", ImportAsync).RequireScope(PricingConstants.Scopes.CostUpdate);
        group.MapPost("/seed-defaults", SeedDefaultsAsync).RequireScope(PricingConstants.Scopes.CostUpdate);
        group.MapPut("/batch", UpdateBatchAsync).RequireScope(PricingConstants.Scopes.CostUpdate);

        return app;
    }

    private static async Task<IResult> BrowseAsync(
        string? shipmentMode,
        ServiceDbContext db,
        CancellationToken cancellationToken
    )
    {
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();

        var mode = NormalizeShipmentMode(shipmentMode, allowEmpty: true);
        command.CommandText = SelectColumns + """
            WHERE (@shipment_mode = '' OR lower(shipment_mode) = lower(@shipment_mode))
            ORDER BY
                CASE lower(shipment_mode) WHEN 'ftl' THEN 0 WHEN 'ltl' THEN 1 ELSE 2 END,
                CASE equipment_class WHEN '48_53' THEN 0 WHEN '5_7_TON' THEN 1 WHEN 'LTL_CBM' THEN 2 ELSE 3 END,
                origin_name,
                destination_name;
            """;
        Add(command, "shipment_mode", mode ?? string.Empty);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<FtlTariffDto>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return Results.Ok(rows);
    }

    private static async Task<IResult> ResolveAsync(
        Guid? originId,
        Guid? destinationId,
        string? equipmentClass,
        string? shipmentMode,
        string? originName,
        string? destinationName,
        string? originCode,
        string? destinationCode,
        DateTime? quoteDate,
        ServiceDbContext db,
        CancellationToken cancellationToken
    )
    {
        var mode = NormalizeShipmentMode(shipmentMode, allowEmpty: true)
            ?? (string.Equals(equipmentClass, "LTL_CBM", StringComparison.OrdinalIgnoreCase) ? "Ltl" : "Ftl");
        var resolvedEquipmentClass = string.IsNullOrWhiteSpace(equipmentClass)
            ? (string.Equals(mode, "Ltl", StringComparison.OrdinalIgnoreCase) ? "LTL_CBM" : string.Empty)
            : equipmentClass.Trim();

        if (string.IsNullOrWhiteSpace(resolvedEquipmentClass))
        {
            return Results.BadRequest(
                new
                {
                    code = "Pricing.LandEquipmentClassRequired",
                    message = "La clase de equipo o base tarifaria terrestre es obligatoria.",
                }
            );
        }

        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + """
            WHERE is_active = TRUE
              AND lower(shipment_mode) = lower(@shipment_mode)
              AND upper(equipment_class) = upper(@equipment_class)
              AND (valid_from IS NULL OR valid_from <= @quote_date)
              AND (valid_to IS NULL OR valid_to >= @quote_date)
              AND
              (
                  (origin_id = @origin_id AND destination_id = @destination_id)
                  OR
                  (
                      @origin_code <> ''
                      AND @destination_code <> ''
                      AND lower(trim(COALESCE(origin_code, ''))) = lower(trim(@origin_code))
                      AND lower(trim(COALESCE(destination_code, ''))) = lower(trim(@destination_code))
                  )
                  OR
                  (
                      @origin_code <> ''
                      AND @destination_code <> ''
                      AND length(trim(COALESCE(origin_code, ''))) >= 2
                      AND length(trim(COALESCE(destination_code, ''))) >= 2
                      AND left(upper(trim(origin_code)), 2) = left(upper(trim(@origin_code)), 2)
                      AND left(upper(trim(destination_code)), 2) = left(upper(trim(@destination_code)), 2)
                  )
                  OR
                  (
                      lower(translate(trim(origin_name), 'áéíóúüñ', 'aeiouun'))
                          = lower(translate(trim(@origin_name), 'áéíóúüñ', 'aeiouun'))
                      AND lower(translate(trim(destination_name), 'áéíóúüñ', 'aeiouun'))
                          = lower(translate(trim(@destination_name), 'áéíóúüñ', 'aeiouun'))
                  )
                  OR
                  (
                      lower(translate(@origin_name, 'áéíóúüñ', 'aeiouun'))
                          LIKE '%' || lower(translate(trim(origin_name), 'áéíóúüñ', 'aeiouun')) || '%'
                      AND lower(translate(@destination_name, 'áéíóúüñ', 'aeiouun'))
                          LIKE '%' || lower(translate(trim(destination_name), 'áéíóúüñ', 'aeiouun')) || '%'
                  )
                  OR
                  (
                      lower(translate(trim(origin_name), 'áéíóúüñ', 'aeiouun'))
                          LIKE '%' || lower(translate(trim(@origin_name), 'áéíóúüñ', 'aeiouun')) || '%'
                      AND lower(translate(trim(destination_name), 'áéíóúüñ', 'aeiouun'))
                          LIKE '%' || lower(translate(trim(@destination_name), 'áéíóúüñ', 'aeiouun')) || '%'
                  )
              )
            ORDER BY
                CASE
                    WHEN origin_id = @origin_id AND destination_id = @destination_id THEN 0
                    WHEN
                        @origin_code <> ''
                        AND @destination_code <> ''
                        AND lower(trim(COALESCE(origin_code, ''))) = lower(trim(@origin_code))
                        AND lower(trim(COALESCE(destination_code, ''))) = lower(trim(@destination_code))
                    THEN 1
                    WHEN
                        @origin_code <> ''
                        AND @destination_code <> ''
                        AND length(trim(COALESCE(origin_code, ''))) >= 2
                        AND length(trim(COALESCE(destination_code, ''))) >= 2
                        AND left(upper(trim(origin_code)), 2) = left(upper(trim(@origin_code)), 2)
                        AND left(upper(trim(destination_code)), 2) = left(upper(trim(@destination_code)), 2)
                    THEN 2
                    WHEN
                        lower(translate(trim(origin_name), 'áéíóúüñ', 'aeiouun'))
                            = lower(translate(trim(@origin_name), 'áéíóúüñ', 'aeiouun'))
                        AND lower(translate(trim(destination_name), 'áéíóúüñ', 'aeiouun'))
                            = lower(translate(trim(@destination_name), 'áéíóúüñ', 'aeiouun'))
                    THEN 3
                    ELSE 4
                END,
                COALESCE(updated_at_utc, created_at_utc) DESC
            LIMIT 1;
            """;

        Add(command, "shipment_mode", mode);
        Add(command, "equipment_class", resolvedEquipmentClass);
        Add(command, "origin_id", originId ?? Guid.Empty);
        Add(command, "destination_id", destinationId ?? Guid.Empty);
        Add(command, "origin_name", originName?.Trim() ?? string.Empty);
        Add(command, "destination_name", destinationName?.Trim() ?? string.Empty);
        Add(command, "origin_code", originCode?.Trim() ?? string.Empty);
        Add(command, "destination_code", destinationCode?.Trim() ?? string.Empty);
        Add(command, "quote_date", (quoteDate ?? DateTime.UtcNow).Date);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Results.Ok((FtlTariffDto?)null);
        }

        return Results.Ok(Read(reader));
    }

    private static async Task<IResult> SeedDefaultsAsync(
        IServiceProvider services,
        ServiceDbContext db,
        CancellationToken cancellationToken
    )
    {
        await TigsaFtlTariffSeeder.SeedAsync(services, cancellationToken);

        var total = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*)::int AS \\"Value\\" FROM pricing.\\\"FtlTariffs\\\";")
            .SingleAsync(cancellationToken);
        var ftl = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*)::int AS \\"Value\\" FROM pricing.\\\"FtlTariffs\\\" WHERE lower(shipment_mode) = 'ftl';")
            .SingleAsync(cancellationToken);
        var ltl = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*)::int AS \\"Value\\" FROM pricing.\\\"FtlTariffs\\\" WHERE lower(shipment_mode) = 'ltl';")
            .SingleAsync(cancellationToken);

        return Results.Ok(new
        {
            total,
            ftl,
            ltl,
            message = "Se verificaron y cargaron las tarifas base terrestres TIGSA/GCF que faltaban sin sobrescribir cambios manuales.",
        });
    }

    private static async Task<IResult> CreateAsync(
        CreateFtlTariffRequest request,
        ServiceDbContext db,
        CancellationToken cancellationToken
    )
    {
        var validation = Validate(request);
        if (validation is not null) return validation;

        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var result = await UpsertAsync(connection, transaction, request, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(new
        {
            id = result.Id,
            created = result.Created,
            message = result.Created ? "Tarifa terrestre creada." : "Tarifa terrestre actualizada.",
        });
    }

    private static async Task<IResult> ImportAsync(
        ImportFtlTariffsRequest request,
        ServiceDbContext db,
        CancellationToken cancellationToken
    )
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new
            {
                code = "Pricing.LandTariffImportItemsRequired",
                message = "Debe enviar al menos una tarifa terrestre para importar.",
            });
        }

        if (request.Items.Count > 1000)
        {
            return Results.BadRequest(new
            {
                code = "Pricing.LandTariffImportTooLarge",
                message = "No se pueden importar más de 1000 tarifas terrestres por operación.",
            });
        }

        foreach (var item in request.Items)
        {
            var validation = Validate(item);
            if (validation is not null) return validation;
        }

        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var created = 0;
        var updated = 0;
        foreach (var item in request.Items)
        {
            var result = await UpsertAsync(connection, transaction, item, cancellationToken);
            if (result.Created) created++;
            else updated++;
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { created, updated, total = created + updated });
    }

    private static async Task<IResult> UpdateBatchAsync(
        UpdateFtlTariffsRequest request,
        ServiceDbContext db,
        CancellationToken cancellationToken
    )
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(
                new
                {
                    code = "Pricing.LandTariffItemsRequired",
                    message = "Debe enviar al menos una tarifa terrestre para actualizar.",
                }
            );
        }

        if (request.Items.Count > 500)
        {
            return Results.BadRequest(
                new
                {
                    code = "Pricing.LandTariffBatchTooLarge",
                    message = "No se pueden actualizar más de 500 tarifas terrestres por operación.",
                }
            );
        }

        foreach (var item in request.Items)
        {
            if (item.Id == Guid.Empty)
            {
                return Results.BadRequest(new
                {
                    code = "Pricing.LandTariffIdRequired",
                    message = "Una de las tarifas terrestres no tiene un identificador válido.",
                });
            }

            if (item.PriceAmount < 0m || item.MinimumAmount is < 0m)
            {
                return Results.BadRequest(new
                {
                    code = "Pricing.LandTariffPriceInvalid",
                    message = "El precio y el mínimo de una tarifa terrestre no pueden ser negativos.",
                });
            }

            if (item.TransitDays is < 0)
            {
                return Results.BadRequest(new
                {
                    code = "Pricing.LandTariffTransitInvalid",
                    message = "Los días de tránsito de una tarifa terrestre no pueden ser negativos.",
                });
            }

            if (item.ValidFrom.HasValue && item.ValidTo.HasValue && item.ValidFrom.Value.Date > item.ValidTo.Value.Date)
            {
                return Results.BadRequest(new
                {
                    code = "Pricing.LandTariffValidityInvalid",
                    message = "La fecha de inicio de vigencia no puede ser posterior a la fecha final.",
                });
            }
        }

        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var item in request.Items)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE pricing."FtlTariffs"
                SET price_amount = @price_amount,
                    minimum_amount = @minimum_amount,
                    transit_days = @transit_days,
                    warehouse_name = @warehouse_name,
                    source = @source,
                    notes = @notes,
                    valid_from = @valid_from,
                    valid_to = @valid_to,
                    is_active = @is_active,
                    updated_at_utc = now()
                WHERE id = @id;
                """;
            Add(command, "id", item.Id);
            Add(command, "price_amount", item.PriceAmount);
            Add(command, "minimum_amount", item.MinimumAmount);
            Add(command, "transit_days", item.TransitDays);
            Add(command, "warehouse_name", NullIfBlank(item.WarehouseName));
            Add(command, "source", NullIfBlank(item.Source));
            Add(command, "notes", NullIfBlank(item.Notes));
            Add(command, "valid_from", item.ValidFrom?.Date);
            Add(command, "valid_to", item.ValidTo?.Date);
            Add(command, "is_active", item.IsActive);

            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Results.NotFound(
                    new
                    {
                        code = "Pricing.LandTariffNotFound",
                        message = $"No se encontró la tarifa terrestre {item.Id}.",
                    }
                );
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static IResult? Validate(CreateFtlTariffRequest item)
    {
        if (string.IsNullOrWhiteSpace(item.OriginName) || string.IsNullOrWhiteSpace(item.DestinationName))
        {
            return Results.BadRequest(new
            {
                code = "Pricing.LandTariffRouteRequired",
                message = "Origen y destino son obligatorios para una tarifa terrestre.",
            });
        }

        var mode = NormalizeShipmentMode(item.ShipmentMode, allowEmpty: false);
        if (mode is null)
        {
            return Results.BadRequest(new
            {
                code = "Pricing.LandTariffModeInvalid",
                message = "La modalidad terrestre debe ser Ftl o Ltl.",
            });
        }

        if (string.IsNullOrWhiteSpace(item.EquipmentClass))
        {
            return Results.BadRequest(new
            {
                code = "Pricing.LandTariffEquipmentRequired",
                message = "La clase de equipo o base tarifaria es obligatoria.",
            });
        }

        if (item.CurrencyId == Guid.Empty || string.IsNullOrWhiteSpace(item.CurrencyCode))
        {
            return Results.BadRequest(new
            {
                code = "Pricing.LandTariffCurrencyRequired",
                message = "La moneda es obligatoria.",
            });
        }

        if (item.PriceAmount < 0m || item.MinimumAmount is < 0m)
        {
            return Results.BadRequest(new
            {
                code = "Pricing.LandTariffPriceInvalid",
                message = "El precio y el mínimo no pueden ser negativos.",
            });
        }

        if (item.TransitDays is < 0)
        {
            return Results.BadRequest(new
            {
                code = "Pricing.LandTariffTransitInvalid",
                message = "Los días de tránsito no pueden ser negativos.",
            });
        }

        if (item.ValidFrom.HasValue && item.ValidTo.HasValue && item.ValidFrom.Value.Date > item.ValidTo.Value.Date)
        {
            return Results.BadRequest(new
            {
                code = "Pricing.LandTariffValidityInvalid",
                message = "La fecha de inicio de vigencia no puede ser posterior a la fecha final.",
            });
        }

        return null;
    }

    private static async Task<(Guid Id, bool Created)> UpsertAsync(
        DbConnection connection,
        DbTransaction transaction,
        CreateFtlTariffRequest item,
        CancellationToken cancellationToken
    )
    {
        var mode = NormalizeShipmentMode(item.ShipmentMode, allowEmpty: false)!;
        var rateBasis = NormalizeRateBasis(item.RateBasis, mode);
        var equipmentClass = item.EquipmentClass.Trim().ToUpperInvariant();
        var equipmentLabel = string.IsNullOrWhiteSpace(item.EquipmentLabel)
            ? (string.Equals(mode, "Ltl", StringComparison.OrdinalIgnoreCase) ? "LTL · USD/CBM" : "Equipo FTL")
            : item.EquipmentLabel.Trim();

        await using var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = """
            SELECT id
            FROM pricing."FtlTariffs"
            WHERE lower(shipment_mode) = lower(@shipment_mode)
              AND upper(equipment_class) = upper(@equipment_class)
              AND lower(translate(trim(origin_name), 'áéíóúüñ', 'aeiouun'))
                  = lower(translate(trim(@origin_name), 'áéíóúüñ', 'aeiouun'))
              AND lower(translate(trim(destination_name), 'áéíóúüñ', 'aeiouun'))
                  = lower(translate(trim(@destination_name), 'áéíóúüñ', 'aeiouun'))
            LIMIT 1;
            """;
        Add(lookup, "shipment_mode", mode);
        Add(lookup, "equipment_class", equipmentClass);
        Add(lookup, "origin_name", item.OriginName.Trim());
        Add(lookup, "destination_name", item.DestinationName.Trim());

        var existing = await lookup.ExecuteScalarAsync(cancellationToken);
        var id = existing is Guid existingId ? existingId : Guid.NewGuid();
        var created = existing is null || existing == DBNull.Value;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = created
            ? """
                INSERT INTO pricing."FtlTariffs"
                (
                    id, origin_id, origin_name, origin_code,
                    destination_id, destination_name, destination_code,
                    shipment_mode, equipment_class, equipment_label,
                    currency_id, currency_name, currency_code,
                    price_amount, rate_basis, minimum_amount, transit_days,
                    warehouse_name, source, notes, valid_from, valid_to,
                    is_active, created_at_utc
                )
                VALUES
                (
                    @id, @origin_id, @origin_name, @origin_code,
                    @destination_id, @destination_name, @destination_code,
                    @shipment_mode, @equipment_class, @equipment_label,
                    @currency_id, @currency_name, @currency_code,
                    @price_amount, @rate_basis, @minimum_amount, @transit_days,
                    @warehouse_name, @source, @notes, @valid_from, @valid_to,
                    @is_active, now()
                );
                """
            : """
                UPDATE pricing."FtlTariffs"
                SET origin_id = @origin_id,
                    origin_code = @origin_code,
                    destination_id = @destination_id,
                    destination_code = @destination_code,
                    equipment_label = @equipment_label,
                    currency_id = @currency_id,
                    currency_name = @currency_name,
                    currency_code = @currency_code,
                    price_amount = @price_amount,
                    rate_basis = @rate_basis,
                    minimum_amount = @minimum_amount,
                    transit_days = @transit_days,
                    warehouse_name = @warehouse_name,
                    source = @source,
                    notes = @notes,
                    valid_from = @valid_from,
                    valid_to = @valid_to,
                    is_active = @is_active,
                    updated_at_utc = now()
                WHERE id = @id;
                """;

        Add(command, "id", id);
        Add(command, "origin_id", item.OriginId);
        Add(command, "origin_name", item.OriginName.Trim());
        Add(command, "origin_code", NullIfBlank(item.OriginCode));
        Add(command, "destination_id", item.DestinationId);
        Add(command, "destination_name", item.DestinationName.Trim());
        Add(command, "destination_code", NullIfBlank(item.DestinationCode));
        Add(command, "shipment_mode", mode);
        Add(command, "equipment_class", equipmentClass);
        Add(command, "equipment_label", equipmentLabel);
        Add(command, "currency_id", item.CurrencyId);
        Add(command, "currency_name", string.IsNullOrWhiteSpace(item.CurrencyName) ? item.CurrencyCode.Trim() : item.CurrencyName.Trim());
        Add(command, "currency_code", item.CurrencyCode.Trim().ToUpperInvariant());
        Add(command, "price_amount", item.PriceAmount);
        Add(command, "rate_basis", rateBasis);
        Add(command, "minimum_amount", item.MinimumAmount);
        Add(command, "transit_days", item.TransitDays);
        Add(command, "warehouse_name", NullIfBlank(item.WarehouseName));
        Add(command, "source", NullIfBlank(item.Source));
        Add(command, "notes", NullIfBlank(item.Notes));
        Add(command, "valid_from", item.ValidFrom?.Date);
        Add(command, "valid_to", item.ValidTo?.Date);
        Add(command, "is_active", item.IsActive);

        await command.ExecuteNonQueryAsync(cancellationToken);
        return (id, created);
    }

    private const string SelectColumns = """
        SELECT
            id,
            origin_id,
            origin_name,
            origin_code,
            destination_id,
            destination_name,
            destination_code,
            equipment_class,
            equipment_label,
            currency_id,
            currency_name,
            currency_code,
            price_amount,
            transit_days,
            source,
            notes,
            is_active,
            shipment_mode,
            rate_basis,
            minimum_amount,
            warehouse_name,
            valid_from,
            valid_to
        FROM pricing."FtlTariffs"
        """;

    private static FtlTariffDto Read(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetGuid(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetDecimal(12),
            reader.IsDBNull(13) ? null : reader.GetInt32(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.IsDBNull(15) ? null : reader.GetString(15),
            reader.GetBoolean(16),
            reader.IsDBNull(17) ? "Ftl" : reader.GetString(17),
            reader.IsDBNull(18) ? "PerTruck" : reader.GetString(18),
            reader.IsDBNull(19) ? null : reader.GetDecimal(19),
            reader.IsDBNull(20) ? null : reader.GetString(20),
            reader.IsDBNull(21) ? null : reader.GetDateTime(21),
            reader.IsDBNull(22) ? null : reader.GetDateTime(22)
        );

    private static string? NormalizeShipmentMode(string? value, bool allowEmpty)
    {
        if (string.IsNullOrWhiteSpace(value)) return allowEmpty ? null : null;
        if (string.Equals(value.Trim(), "Ftl", StringComparison.OrdinalIgnoreCase)) return "Ftl";
        if (string.Equals(value.Trim(), "Ltl", StringComparison.OrdinalIgnoreCase)) return "Ltl";
        return null;
    }

    private static string NormalizeRateBasis(string? value, string shipmentMode)
    {
        if (string.Equals(value?.Trim(), "PerCbm", StringComparison.OrdinalIgnoreCase)) return "PerCbm";
        if (string.Equals(value?.Trim(), "PerTruck", StringComparison.OrdinalIgnoreCase)) return "PerTruck";
        return string.Equals(shipmentMode, "Ltl", StringComparison.OrdinalIgnoreCase) ? "PerCbm" : "PerTruck";
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task EnsureOpenAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}

public sealed record FtlTariffDto(
    Guid Id,
    Guid? OriginId,
    string OriginName,
    string? OriginCode,
    Guid? DestinationId,
    string DestinationName,
    string? DestinationCode,
    string EquipmentClass,
    string EquipmentLabel,
    Guid CurrencyId,
    string CurrencyName,
    string CurrencyCode,
    decimal PriceAmount,
    int? TransitDays,
    string? Source,
    string? Notes,
    bool IsActive,
    string ShipmentMode,
    string RateBasis,
    decimal? MinimumAmount,
    string? WarehouseName,
    DateTime? ValidFrom,
    DateTime? ValidTo
);

public sealed record CreateFtlTariffRequest(
    Guid? OriginId,
    string OriginName,
    string? OriginCode,
    Guid? DestinationId,
    string DestinationName,
    string? DestinationCode,
    string ShipmentMode,
    string EquipmentClass,
    string EquipmentLabel,
    Guid CurrencyId,
    string CurrencyName,
    string CurrencyCode,
    decimal PriceAmount,
    string? RateBasis = null,
    decimal? MinimumAmount = null,
    int? TransitDays = null,
    string? WarehouseName = null,
    string? Source = null,
    string? Notes = null,
    DateTime? ValidFrom = null,
    DateTime? ValidTo = null,
    bool IsActive = true
);

public sealed record ImportFtlTariffsRequest(IReadOnlyCollection<CreateFtlTariffRequest> Items);

public sealed record UpdateFtlTariffsRequest(IReadOnlyCollection<UpdateFtlTariffItemRequest> Items);

public sealed record UpdateFtlTariffItemRequest(
    Guid Id,
    decimal PriceAmount,
    decimal? MinimumAmount,
    int? TransitDays,
    string? WarehouseName,
    string? Source,
    string? Notes,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    bool IsActive
);
