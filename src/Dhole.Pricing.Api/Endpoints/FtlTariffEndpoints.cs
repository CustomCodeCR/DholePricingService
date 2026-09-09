using System.Data.Common;
using Dhole.Pricing.Api.Authorization;
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
            .WithTags("FTL tariffs")
            .RequireAuthorization();

        group.MapGet("/", BrowseAsync).RequireScope(PricingConstants.Scopes.CostView);
        group.MapGet("/resolve", ResolveAsync).RequireScope(PricingConstants.Scopes.CostSelect);
        group.MapPut("/batch", UpdateBatchAsync).RequireScope(PricingConstants.Scopes.CostUpdate);

        return app;
    }

    private static async Task<IResult> BrowseAsync(
        ServiceDbContext db,
        CancellationToken cancellationToken
    )
    {
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + """
            ORDER BY
                CASE equipment_class WHEN '48_53' THEN 0 WHEN '5_7_TON' THEN 1 ELSE 2 END,
                origin_name,
                destination_name;
            """;

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
        string? originName,
        string? destinationName,
        string? originCode,
        string? destinationCode,
        ServiceDbContext db,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(equipmentClass))
        {
            return Results.BadRequest(
                new
                {
                    code = "Pricing.FtlEquipmentClassRequired",
                    message = "La clase de equipo FTL es obligatoria.",
                }
            );
        }

        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + """
            WHERE is_active = TRUE
              AND upper(equipment_class) = upper(@equipment_class)
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
                      lower(trim(origin_name)) = lower(trim(@origin_name))
                      AND lower(trim(destination_name)) = lower(trim(@destination_name))
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
                    ELSE 2
                END,
                COALESCE(updated_at_utc, created_at_utc) DESC
            LIMIT 1;
            """;

        Add(command, "equipment_class", equipmentClass.Trim());
        Add(command, "origin_id", originId ?? Guid.Empty);
        Add(command, "destination_id", destinationId ?? Guid.Empty);
        Add(command, "origin_name", originName?.Trim() ?? string.Empty);
        Add(command, "destination_name", destinationName?.Trim() ?? string.Empty);
        Add(command, "origin_code", originCode?.Trim() ?? string.Empty);
        Add(command, "destination_code", destinationCode?.Trim() ?? string.Empty);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Results.Ok((FtlTariffDto?)null);
        }

        return Results.Ok(Read(reader));
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
                    code = "Pricing.FtlTariffItemsRequired",
                    message = "Debe enviar al menos una tarifa FTL para actualizar.",
                }
            );
        }

        if (request.Items.Count > 250)
        {
            return Results.BadRequest(
                new
                {
                    code = "Pricing.FtlTariffBatchTooLarge",
                    message = "No se pueden actualizar más de 250 tarifas FTL por operación.",
                }
            );
        }

        foreach (var item in request.Items)
        {
            if (item.Id == Guid.Empty)
            {
                return Results.BadRequest(
                    new
                    {
                        code = "Pricing.FtlTariffIdRequired",
                        message = "Una de las tarifas FTL no tiene un identificador válido.",
                    }
                );
            }

            if (item.PriceAmount < 0m)
            {
                return Results.BadRequest(
                    new
                    {
                        code = "Pricing.FtlTariffPriceInvalid",
                        message = "El precio de una tarifa FTL no puede ser negativo.",
                    }
                );
            }

            if (item.TransitDays is < 0)
            {
                return Results.BadRequest(
                    new
                    {
                        code = "Pricing.FtlTariffTransitInvalid",
                        message = "Los días de tránsito de una tarifa FTL no pueden ser negativos.",
                    }
                );
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
                    transit_days = @transit_days,
                    is_active = @is_active,
                    updated_at_utc = now()
                WHERE id = @id;
                """;
            Add(command, "id", item.Id);
            Add(command, "price_amount", item.PriceAmount);
            Add(command, "transit_days", item.TransitDays);
            Add(command, "is_active", item.IsActive);

            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Results.NotFound(
                    new
                    {
                        code = "Pricing.FtlTariffNotFound",
                        message = $"No se encontró la tarifa FTL {item.Id}.",
                    }
                );
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
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
            is_active
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
            reader.GetBoolean(16)
        );

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
    bool IsActive
);

public sealed record UpdateFtlTariffsRequest(IReadOnlyCollection<UpdateFtlTariffItemRequest> Items);

public sealed record UpdateFtlTariffItemRequest(
    Guid Id,
    decimal PriceAmount,
    int? TransitDays,
    bool IsActive
);
