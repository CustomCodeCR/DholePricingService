using System.Data.Common;
using System.Text.Json;
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
        group.MapPost("/", CreateAsync).RequireIdempotency().RequireScope(PricingConstants.Scopes.CostUpdate);
        group.MapPut("/{id:guid}", UpdateAsync).RequireScope(PricingConstants.Scopes.CostUpdate);
        group.MapPost("/import", ImportAsync).RequireIdempotency().RequireScope(PricingConstants.Scopes.CostUpdate);
        group.MapPost("/seed-defaults", SeedDefaultsAsync).RequireIdempotency().RequireScope(PricingConstants.Scopes.CostUpdate);
        group.MapPut("/batch", UpdateBatchAsync).RequireScope(PricingConstants.Scopes.CostUpdate);

        return app;
    }

    private static async Task<IResult> BrowseAsync(
        string? shipmentMode,
        string? commercialProfile,
        ServiceDbContext db,
        CancellationToken cancellationToken
    )
    {
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();

        var mode = NormalizeShipmentMode(shipmentMode, allowEmpty: true);
        var profile = NormalizeCommercialProfile(commercialProfile, mode, allowEmpty: true);
        command.CommandText = SelectColumns + """
            WHERE (@shipment_mode = '' OR lower(shipment_mode) = lower(@shipment_mode))
              AND (@commercial_profile = '' OR lower(commercial_profile) = lower(@commercial_profile))
            ORDER BY
                CASE lower(shipment_mode) WHEN 'ftl' THEN 0 WHEN 'ltl' THEN 1 ELSE 2 END,
                CASE equipment_class WHEN '48_53' THEN 0 WHEN '5_7_TON' THEN 1 WHEN 'LTL_CBM' THEN 2 ELSE 3 END,
                origin_name,
                destination_name;
            """;
        Add(command, "shipment_mode", mode ?? string.Empty);
        Add(command, "commercial_profile", profile ?? string.Empty);

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
        string? commercialProfile,
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
        var profile = NormalizeCommercialProfile(commercialProfile, mode, allowEmpty: false);

        if (profile is null)
        {
            return Results.BadRequest(
                new
                {
                    code = "Pricing.LandCommercialProfileInvalid",
                    message = "El perfil comercial terrestre debe ser General, FinalClient o Nvocc según la modalidad.",
                }
            );
        }

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
              AND lower(commercial_profile) = lower(@commercial_profile)
              AND
              (
                  (
                      NULLIF(trim(COALESCE(applicable_equipment_classes, '')), '') IS NOT NULL
                      AND EXISTS
                      (
                          SELECT 1
                          FROM jsonb_array_elements_text(applicable_equipment_classes::jsonb) AS applicable(value)
                          WHERE upper(trim(applicable.value)) = upper(@equipment_class)
                      )
                  )
                  OR
                  (
                      NULLIF(trim(COALESCE(applicable_equipment_classes, '')), '') IS NULL
                      AND upper(equipment_class) = upper(@equipment_class)
                  )
              )
              AND (valid_from IS NULL OR valid_from <= @quote_date)
              AND (valid_to IS NULL OR valid_to >= @quote_date)
              AND
              (
                  (
                      (
                          origin_id = @origin_id
                          OR
                          (
                              @origin_id_text <> ''
                              AND NULLIF(trim(COALESCE(applicable_origin_ids, '')), '') IS NOT NULL
                              AND EXISTS
                              (
                                  SELECT 1
                                  FROM jsonb_array_elements_text(applicable_origin_ids::jsonb) AS applicable_origin(value)
                                  WHERE lower(trim(applicable_origin.value)) = lower(@origin_id_text)
                              )
                          )
                      )
                      AND
                      (
                          destination_id = @destination_id
                          OR
                          (
                              @destination_id_text <> ''
                              AND NULLIF(trim(COALESCE(applicable_destination_ids, '')), '') IS NOT NULL
                              AND EXISTS
                              (
                                  SELECT 1
                                  FROM jsonb_array_elements_text(applicable_destination_ids::jsonb) AS applicable_destination(value)
                                  WHERE lower(trim(applicable_destination.value)) = lower(@destination_id_text)
                              )
                          )
                      )
                  )
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
                    WHEN
                        (
                            origin_id = @origin_id
                            OR
                            (
                                @origin_id_text <> ''
                                AND NULLIF(trim(COALESCE(applicable_origin_ids, '')), '') IS NOT NULL
                                AND EXISTS
                                (
                                    SELECT 1
                                    FROM jsonb_array_elements_text(applicable_origin_ids::jsonb) AS applicable_origin(value)
                                    WHERE lower(trim(applicable_origin.value)) = lower(@origin_id_text)
                                )
                            )
                        )
                        AND
                        (
                            destination_id = @destination_id
                            OR
                            (
                                @destination_id_text <> ''
                                AND NULLIF(trim(COALESCE(applicable_destination_ids, '')), '') IS NOT NULL
                                AND EXISTS
                                (
                                    SELECT 1
                                    FROM jsonb_array_elements_text(applicable_destination_ids::jsonb) AS applicable_destination(value)
                                    WHERE lower(trim(applicable_destination.value)) = lower(@destination_id_text)
                                )
                            )
                        )
                    THEN 0
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
        Add(command, "commercial_profile", profile);
        Add(command, "equipment_class", resolvedEquipmentClass);
        Add(command, "origin_id", originId ?? Guid.Empty);
        Add(command, "destination_id", destinationId ?? Guid.Empty);
        Add(command, "origin_id_text", originId?.ToString() ?? string.Empty);
        Add(command, "destination_id_text", destinationId?.ToString() ?? string.Empty);
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

        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);

        async Task<int> CountAsync(string whereClause)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*)::int FROM pricing.\"FtlTariffs\" {whereClause};";
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is null || value == DBNull.Value ? 0 : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        var total = await CountAsync(string.Empty);
        var ftl = await CountAsync("WHERE lower(shipment_mode) = 'ftl'");
        var ltl = await CountAsync("WHERE lower(shipment_mode) = 'ltl'");
        var ltlFinalClient = await CountAsync("WHERE lower(shipment_mode) = 'ltl' AND lower(commercial_profile) = 'finalclient'");
        var ltlNvocc = await CountAsync("WHERE lower(shipment_mode) = 'ltl' AND lower(commercial_profile) = 'nvocc'");

        return Results.Ok(new
        {
            total,
            ftl,
            ltl,
            ltlFinalClient,
            ltlNvocc,
            message = "Se verificó la matriz LTL GCF (Cliente final y NVOCC). Las tarifas FTL se administran únicamente de forma manual.",
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


    private static async Task<IResult> UpdateAsync(
        Guid id,
        CreateFtlTariffRequest request,
        ServiceDbContext db,
        CancellationToken cancellationToken
    )
    {
        if (id == Guid.Empty)
        {
            return Results.BadRequest(new
            {
                code = "Pricing.LandTariffIdRequired",
                message = "La tarifa terrestre requiere un identificador válido.",
            });
        }

        var validation = Validate(request);
        if (validation is not null) return validation;

        var mode = NormalizeShipmentMode(request.ShipmentMode, allowEmpty: false)!;
        var rateBasis = NormalizeRateBasis(request.RateBasis, mode);
        var commercialProfile = NormalizeCommercialProfile(request.CommercialProfile, mode, allowEmpty: false)!;
        var applicableEquipmentClasses = NormalizeApplicableEquipmentClasses(
            request.ApplicableEquipmentClasses,
            mode,
            request.EquipmentClass
        );
        var equipmentClass = string.Equals(mode, "Ltl", StringComparison.OrdinalIgnoreCase)
            ? "LTL_CBM"
            : applicableEquipmentClasses.First();
        var equipmentLabel = string.IsNullOrWhiteSpace(request.EquipmentLabel)
            ? (string.Equals(mode, "Ltl", StringComparison.OrdinalIgnoreCase)
                ? "LTL · USD/CBM"
                : $"{applicableEquipmentClasses.Count} equipo{(applicableEquipmentClasses.Count == 1 ? string.Empty : "s")} aplicable{(applicableEquipmentClasses.Count == 1 ? string.Empty : "s")}")
            : request.EquipmentLabel.Trim();
        var applicableOriginIds = NormalizeApplicableRouteIds(request.ApplicableOriginIds, request.OriginId);
        var applicableDestinationIds = NormalizeApplicableRouteIds(request.ApplicableDestinationIds, request.DestinationId);
        var primaryOriginId = request.OriginId
            ?? (applicableOriginIds.Count > 0 ? applicableOriginIds.First() : (Guid?)null);
        var primaryDestinationId = request.DestinationId
            ?? (applicableDestinationIds.Count > 0 ? applicableDestinationIds.First() : (Guid?)null);
        var ltlChargeItems = NormalizeLtlChargeItems(
            request.LtlChargeItems,
            mode,
            commercialProfile
        );

        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE pricing."FtlTariffs"
            SET origin_id = @origin_id,
                origin_name = @origin_name,
                origin_code = @origin_code,
                applicable_origin_ids = @applicable_origin_ids,
                destination_id = @destination_id,
                destination_name = @destination_name,
                destination_code = @destination_code,
                applicable_destination_ids = @applicable_destination_ids,
                ltl_charge_items = @ltl_charge_items,
                shipment_mode = @shipment_mode,
                commercial_profile = @commercial_profile,
                equipment_class = @equipment_class,
                equipment_label = @equipment_label,
                applicable_equipment_classes = @applicable_equipment_classes,
                currency_id = @currency_id,
                currency_name = @currency_name,
                currency_code = @currency_code,
                price_amount = @price_amount,
                rate_basis = @rate_basis,
                minimum_amount = @minimum_amount,
                cost_per_cbm = @cost_per_cbm,
                weight_kg_per_cbm = @weight_kg_per_cbm,
                dua_cost = @dua_cost,
                duca_t_cost = @duca_t_cost,
                stuffing_cost_per_cbm = @stuffing_cost_per_cbm,
                stuffing_sale_per_cbm = @stuffing_sale_per_cbm,
                panama_cost_surcharge_per_cbm = @panama_cost_surcharge_per_cbm,
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
        Add(command, "origin_id", primaryOriginId);
        Add(command, "origin_name", request.OriginName.Trim());
        Add(command, "origin_code", NullIfBlank(request.OriginCode));
        Add(command, "applicable_origin_ids", JsonSerializer.Serialize(applicableOriginIds));
        Add(command, "destination_id", primaryDestinationId);
        Add(command, "destination_name", request.DestinationName.Trim());
        Add(command, "destination_code", NullIfBlank(request.DestinationCode));
        Add(command, "applicable_destination_ids", JsonSerializer.Serialize(applicableDestinationIds));
        Add(command, "ltl_charge_items", mode == "Ltl" ? JsonSerializer.Serialize(ltlChargeItems) : null);
        Add(command, "shipment_mode", mode);
        Add(command, "commercial_profile", commercialProfile);
        Add(command, "equipment_class", equipmentClass);
        Add(command, "equipment_label", equipmentLabel);
        Add(command, "applicable_equipment_classes", JsonSerializer.Serialize(applicableEquipmentClasses));
        Add(command, "currency_id", request.CurrencyId);
        Add(command, "currency_name", string.IsNullOrWhiteSpace(request.CurrencyName) ? request.CurrencyCode.Trim() : request.CurrencyName.Trim());
        Add(command, "currency_code", request.CurrencyCode.Trim().ToUpperInvariant());
        Add(command, "price_amount", request.PriceAmount);
        Add(command, "rate_basis", rateBasis);
        Add(command, "minimum_amount", request.MinimumAmount);
        Add(command, "cost_per_cbm", mode == "Ltl" ? Math.Max(0m, request.CostPerCbm ?? 0m) : null);
        Add(command, "weight_kg_per_cbm", mode == "Ltl" ? (request.WeightKgPerCbm is > 0m ? request.WeightKgPerCbm : 330m) : null);
        Add(command, "dua_cost", mode == "Ltl" ? Math.Max(0m, request.DuaCost ?? 50m) : null);
        Add(command, "duca_t_cost", mode == "Ltl" ? Math.Max(0m, request.DucaTCost ?? 30m) : null);
        Add(command, "stuffing_cost_per_cbm", mode == "Ltl" ? Math.Max(0m, request.StuffingCostPerCbm ?? (550m / 60m)) : null);
        Add(command, "stuffing_sale_per_cbm", mode == "Ltl" ? Math.Max(0m, request.StuffingSalePerCbm ?? 10m) : null);
        Add(command, "panama_cost_surcharge_per_cbm", mode == "Ltl" ? Math.Max(0m, request.PanamaCostSurchargePerCbm ?? 9m) : null);
        Add(command, "transit_days", request.TransitDays);
        Add(command, "warehouse_name", NullIfBlank(request.WarehouseName));
        Add(command, "source", NullIfBlank(request.Source));
        Add(command, "notes", NullIfBlank(request.Notes));
        Add(command, "valid_from", request.ValidFrom?.Date);
        Add(command, "valid_to", request.ValidTo?.Date);
        Add(command, "is_active", request.IsActive);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 0
            ? Results.NotFound(new
            {
                code = "Pricing.LandTariffNotFound",
                message = $"No se encontró la tarifa terrestre {id}.",
            })
            : Results.NoContent();
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
                    cost_per_cbm = @cost_per_cbm,
                    weight_kg_per_cbm = @weight_kg_per_cbm,
                    dua_cost = @dua_cost,
                    duca_t_cost = @duca_t_cost,
                    stuffing_cost_per_cbm = @stuffing_cost_per_cbm,
                    stuffing_sale_per_cbm = @stuffing_sale_per_cbm,
                    panama_cost_surcharge_per_cbm = @panama_cost_surcharge_per_cbm,
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

        var profile = NormalizeCommercialProfile(item.CommercialProfile, mode, allowEmpty: false);
        if (profile is null)
        {
            return Results.BadRequest(new
            {
                code = "Pricing.LandCommercialProfileInvalid",
                message = "Para LTL indique FinalClient o Nvocc. Para FTL se utiliza General.",
            });
        }

        var applicableEquipmentClasses = NormalizeApplicableEquipmentClasses(
            item.ApplicableEquipmentClasses,
            mode,
            item.EquipmentClass
        );
        if (applicableEquipmentClasses.Count == 0)
        {
            return Results.BadRequest(new
            {
                code = "Pricing.LandTariffEquipmentRequired",
                message = mode == "Ltl"
                    ? "La tarifa consolidada requiere la base LTL."
                    : "Seleccione al menos un equipo o contenedor aplicable a la tarifa completa.",
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

        if (mode == "Ltl")
        {
            if (item.LtlChargeItems is not null)
            {
                foreach (var charge in item.LtlChargeItems)
                {
                    if (string.IsNullOrWhiteSpace(charge.Key) || string.IsNullOrWhiteSpace(charge.Name))
                    {
                        return Results.BadRequest(new
                        {
                            code = "Pricing.LtlChargeInvalid",
                            message = "Cada cargo LTL requiere clave y nombre.",
                        });
                    }

                    if (charge.CostAmount is < 0m || charge.SaleAmount is < 0m)
                    {
                        return Results.BadRequest(new
                        {
                            code = "Pricing.LtlChargeAmountInvalid",
                            message = "Los montos de los cargos LTL no pueden ser negativos.",
                        });
                    }
                }
            }

            if (item.CostPerCbm is < 0m
                || item.DuaCost is < 0m
                || item.DucaTCost is < 0m
                || item.StuffingCostPerCbm is < 0m
                || item.StuffingSalePerCbm is < 0m
                || item.PanamaCostSurchargePerCbm is < 0m)
            {
                return Results.BadRequest(new
                {
                    code = "Pricing.LtlAmountInvalid",
                    message = "Los montos de costo y venta LTL no pueden ser negativos.",
                });
            }

            if (item.WeightKgPerCbm.HasValue && item.WeightKgPerCbm.Value <= 0m)
            {
                return Results.BadRequest(new
                {
                    code = "Pricing.LtlWeightFactorInvalid",
                    message = "El factor de peso LTL debe ser mayor a cero.",
                });
            }
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
        var commercialProfile = NormalizeCommercialProfile(item.CommercialProfile, mode, allowEmpty: false)!;
        var applicableEquipmentClasses = NormalizeApplicableEquipmentClasses(
            item.ApplicableEquipmentClasses,
            mode,
            item.EquipmentClass
        );
        var equipmentClass = string.Equals(mode, "Ltl", StringComparison.OrdinalIgnoreCase)
            ? "LTL_CBM"
            : applicableEquipmentClasses.First();
        var equipmentLabel = string.IsNullOrWhiteSpace(item.EquipmentLabel)
            ? (string.Equals(mode, "Ltl", StringComparison.OrdinalIgnoreCase) ? "LTL · USD/CBM" : "Equipo FTL")
            : item.EquipmentLabel.Trim();
        var applicableOriginIds = NormalizeApplicableRouteIds(item.ApplicableOriginIds, item.OriginId);
        var applicableDestinationIds = NormalizeApplicableRouteIds(item.ApplicableDestinationIds, item.DestinationId);
        var primaryOriginId = item.OriginId
            ?? (applicableOriginIds.Count > 0 ? applicableOriginIds.First() : (Guid?)null);
        var primaryDestinationId = item.DestinationId
            ?? (applicableDestinationIds.Count > 0 ? applicableDestinationIds.First() : (Guid?)null);
        var ltlChargeItems = NormalizeLtlChargeItems(
            item.LtlChargeItems,
            mode,
            commercialProfile
        );

        await using var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = """
            SELECT id
            FROM pricing."FtlTariffs"
            WHERE lower(shipment_mode) = lower(@shipment_mode)
              AND lower(commercial_profile) = lower(@commercial_profile)
              AND upper(equipment_class) = upper(@equipment_class)
              AND lower(translate(trim(origin_name), 'áéíóúüñ', 'aeiouun'))
                  = lower(translate(trim(@origin_name), 'áéíóúüñ', 'aeiouun'))
              AND lower(translate(trim(destination_name), 'áéíóúüñ', 'aeiouun'))
                  = lower(translate(trim(@destination_name), 'áéíóúüñ', 'aeiouun'))
            LIMIT 1;
            """;
        Add(lookup, "shipment_mode", mode);
        Add(lookup, "commercial_profile", commercialProfile);
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
                    id, origin_id, origin_name, origin_code, applicable_origin_ids,
                    destination_id, destination_name, destination_code, applicable_destination_ids,
                    ltl_charge_items,
                    shipment_mode, commercial_profile, equipment_class, equipment_label,
                    applicable_equipment_classes,
                    currency_id, currency_name, currency_code,
                    price_amount, rate_basis, minimum_amount,
                    cost_per_cbm, weight_kg_per_cbm, dua_cost, duca_t_cost,
                    stuffing_cost_per_cbm, stuffing_sale_per_cbm, panama_cost_surcharge_per_cbm,
                    transit_days, warehouse_name, source, notes, valid_from, valid_to,
                    is_active, created_at_utc
                )
                VALUES
                (
                    @id, @origin_id, @origin_name, @origin_code, @applicable_origin_ids,
                    @destination_id, @destination_name, @destination_code, @applicable_destination_ids,
                    @ltl_charge_items,
                    @shipment_mode, @commercial_profile, @equipment_class, @equipment_label,
                    @applicable_equipment_classes,
                    @currency_id, @currency_name, @currency_code,
                    @price_amount, @rate_basis, @minimum_amount,
                    @cost_per_cbm, @weight_kg_per_cbm, @dua_cost, @duca_t_cost,
                    @stuffing_cost_per_cbm, @stuffing_sale_per_cbm, @panama_cost_surcharge_per_cbm,
                    @transit_days, @warehouse_name, @source, @notes, @valid_from, @valid_to,
                    @is_active, now()
                );
                """
            : """
                UPDATE pricing."FtlTariffs"
                SET origin_id = @origin_id,
                    origin_name = @origin_name,
                    origin_code = @origin_code,
                    applicable_origin_ids = @applicable_origin_ids,
                    destination_id = @destination_id,
                    destination_name = @destination_name,
                    destination_code = @destination_code,
                    applicable_destination_ids = @applicable_destination_ids,
                    ltl_charge_items = @ltl_charge_items,
                    commercial_profile = @commercial_profile,
                    equipment_class = @equipment_class,
                    equipment_label = @equipment_label,
                    applicable_equipment_classes = @applicable_equipment_classes,
                    currency_id = @currency_id,
                    currency_name = @currency_name,
                    currency_code = @currency_code,
                    price_amount = @price_amount,
                    rate_basis = @rate_basis,
                    minimum_amount = @minimum_amount,
                    cost_per_cbm = @cost_per_cbm,
                    weight_kg_per_cbm = @weight_kg_per_cbm,
                    dua_cost = @dua_cost,
                    duca_t_cost = @duca_t_cost,
                    stuffing_cost_per_cbm = @stuffing_cost_per_cbm,
                    stuffing_sale_per_cbm = @stuffing_sale_per_cbm,
                    panama_cost_surcharge_per_cbm = @panama_cost_surcharge_per_cbm,
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
        Add(command, "origin_id", primaryOriginId);
        Add(command, "origin_name", item.OriginName.Trim());
        Add(command, "origin_code", NullIfBlank(item.OriginCode));
        Add(command, "applicable_origin_ids", JsonSerializer.Serialize(applicableOriginIds));
        Add(command, "destination_id", primaryDestinationId);
        Add(command, "destination_name", item.DestinationName.Trim());
        Add(command, "destination_code", NullIfBlank(item.DestinationCode));
        Add(command, "applicable_destination_ids", JsonSerializer.Serialize(applicableDestinationIds));
        Add(command, "ltl_charge_items", mode == "Ltl" ? JsonSerializer.Serialize(ltlChargeItems) : null);
        Add(command, "shipment_mode", mode);
        Add(command, "commercial_profile", commercialProfile);
        Add(command, "equipment_class", equipmentClass);
        Add(command, "equipment_label", equipmentLabel);
        Add(command, "applicable_equipment_classes", JsonSerializer.Serialize(applicableEquipmentClasses));
        Add(command, "currency_id", item.CurrencyId);
        Add(command, "currency_name", string.IsNullOrWhiteSpace(item.CurrencyName) ? item.CurrencyCode.Trim() : item.CurrencyName.Trim());
        Add(command, "currency_code", NormalizeCurrencyBusinessCode(item.CurrencyCode, item.CurrencyName));
        Add(command, "price_amount", item.PriceAmount);
        Add(command, "rate_basis", rateBasis);
        Add(command, "minimum_amount", item.MinimumAmount);
        Add(command, "cost_per_cbm", mode == "Ltl" ? Math.Max(0m, item.CostPerCbm ?? 0m) : null);
        Add(command, "weight_kg_per_cbm", mode == "Ltl" ? (item.WeightKgPerCbm is > 0m ? item.WeightKgPerCbm : 330m) : null);
        Add(command, "dua_cost", mode == "Ltl" ? Math.Max(0m, item.DuaCost ?? 50m) : null);
        Add(command, "duca_t_cost", mode == "Ltl" ? Math.Max(0m, item.DucaTCost ?? 30m) : null);
        Add(command, "stuffing_cost_per_cbm", mode == "Ltl" ? Math.Max(0m, item.StuffingCostPerCbm ?? (550m / 60m)) : null);
        Add(command, "stuffing_sale_per_cbm", mode == "Ltl" ? Math.Max(0m, item.StuffingSalePerCbm ?? 10m) : null);
        Add(command, "panama_cost_surcharge_per_cbm", mode == "Ltl" ? Math.Max(0m, item.PanamaCostSurchargePerCbm ?? 9m) : null);
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
            valid_to,
            commercial_profile,
            applicable_equipment_classes,
            cost_per_cbm,
            weight_kg_per_cbm,
            dua_cost,
            duca_t_cost,
            stuffing_cost_per_cbm,
            stuffing_sale_per_cbm,
            panama_cost_surcharge_per_cbm,
            applicable_origin_ids,
            applicable_destination_ids,
            ltl_charge_items
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
            reader.IsDBNull(22) ? null : reader.GetDateTime(22),
            reader.IsDBNull(23) ? "General" : reader.GetString(23),
            ReadApplicableEquipmentClasses(reader, 24, reader.GetString(7)),
            reader.IsDBNull(25) ? null : reader.GetDecimal(25),
            reader.IsDBNull(26) ? null : reader.GetDecimal(26),
            reader.IsDBNull(27) ? null : reader.GetDecimal(27),
            reader.IsDBNull(28) ? null : reader.GetDecimal(28),
            reader.IsDBNull(29) ? null : reader.GetDecimal(29),
            reader.IsDBNull(30) ? null : reader.GetDecimal(30),
            reader.IsDBNull(31) ? null : reader.GetDecimal(31),
            ReadApplicableRouteIds(reader, 32, reader.IsDBNull(1) ? null : reader.GetGuid(1)),
            ReadApplicableRouteIds(reader, 33, reader.IsDBNull(4) ? null : reader.GetGuid(4)),
            ReadLtlChargeItems(
                reader,
                34,
                reader.IsDBNull(17) ? "Ftl" : reader.GetString(17),
                reader.IsDBNull(23) ? "General" : reader.GetString(23)
            )
        );

    private static string? NormalizeShipmentMode(string? value, bool allowEmpty)
    {
        if (string.IsNullOrWhiteSpace(value)) return allowEmpty ? null : null;
        if (string.Equals(value.Trim(), "Ftl", StringComparison.OrdinalIgnoreCase)) return "Ftl";
        if (string.Equals(value.Trim(), "Ltl", StringComparison.OrdinalIgnoreCase)) return "Ltl";
        return null;
    }

    private static string? NormalizeCommercialProfile(string? value, string? shipmentMode, bool allowEmpty)
    {
        var isLtl = string.Equals(shipmentMode, "Ltl", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
            return allowEmpty ? null : (isLtl ? "FinalClient" : "General");

        var normalized = value.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        if (isLtl)
        {
            if (normalized is "finalclient" or "clientefinal" or "client") return "FinalClient";
            if (normalized is "nvocc" or "nvo") return "Nvocc";
            return null;
        }

        return normalized is "general" or "standard" ? "General" : null;
    }

    private static string NormalizeRateBasis(string? value, string shipmentMode)
    {
        if (string.Equals(value?.Trim(), "PerCbm", StringComparison.OrdinalIgnoreCase)) return "PerCbm";
        if (string.Equals(value?.Trim(), "PerTruck", StringComparison.OrdinalIgnoreCase)) return "PerTruck";
        return string.Equals(shipmentMode, "Ltl", StringComparison.OrdinalIgnoreCase) ? "PerCbm" : "PerTruck";
    }


    private static IReadOnlyCollection<string> NormalizeApplicableEquipmentClasses(
        IReadOnlyCollection<string>? values,
        string shipmentMode,
        string? legacyEquipmentClass
    )
    {
        if (string.Equals(shipmentMode, "Ltl", StringComparison.OrdinalIgnoreCase))
            return new[] { "LTL_CBM" };

        var source = values is null
            ? new[] { legacyEquipmentClass ?? string.Empty }
            : values;

        return source
            .Select(value => value?.Trim().ToUpperInvariant() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyCollection<string> ReadApplicableEquipmentClasses(
        DbDataReader reader,
        int index,
        string legacyEquipmentClass
    )
    {
        if (!reader.IsDBNull(index))
        {
            try
            {
                var values = JsonSerializer.Deserialize<string[]>(reader.GetString(index));
                var normalized = values?
                    .Select(value => value?.Trim().ToUpperInvariant() ?? string.Empty)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (normalized is { Length: > 0 }) return normalized;
            }
            catch (JsonException)
            {
                // Rows created before equipment applicability use the legacy class below.
            }
        }

        var fallback = legacyEquipmentClass.Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(fallback) ? Array.Empty<string>() : new[] { fallback };
    }

    private static IReadOnlyCollection<Guid> NormalizeApplicableRouteIds(
        IReadOnlyCollection<Guid>? values,
        Guid? legacyId
    )
    {
        var normalized = values?
            .Where(value => value != Guid.Empty)
            .Distinct()
            .ToArray();

        if (normalized is { Length: > 0 }) return normalized;
        return legacyId.HasValue && legacyId.Value != Guid.Empty
            ? new[] { legacyId.Value }
            : Array.Empty<Guid>();
    }

    private static IReadOnlyCollection<Guid> ReadApplicableRouteIds(
        DbDataReader reader,
        int index,
        Guid? legacyId
    )
    {
        if (!reader.IsDBNull(index))
        {
            try
            {
                var values = JsonSerializer.Deserialize<Guid[]>(reader.GetString(index));
                var normalized = values?
                    .Where(value => value != Guid.Empty)
                    .Distinct()
                    .ToArray();

                if (normalized is { Length: > 0 }) return normalized;
            }
            catch (JsonException)
            {
                // Rows created before route applicability use the legacy route id below.
            }
        }

        return legacyId.HasValue && legacyId.Value != Guid.Empty
            ? new[] { legacyId.Value }
            : Array.Empty<Guid>();
    }

    private static IReadOnlyCollection<LtlChargeItemDto> NormalizeLtlChargeItems(
        IReadOnlyCollection<LtlChargeItemDto>? values,
        string shipmentMode,
        string commercialProfile
    )
    {
        if (!string.Equals(shipmentMode, "Ltl", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<LtlChargeItemDto>();

        var defaults = DefaultLtlChargeItems(commercialProfile).ToList();
        if (values is null || values.Count == 0) return defaults;

        var supplied = values
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < defaults.Count; index++)
        {
            var current = defaults[index];
            if (current.IsFlat || !supplied.TryGetValue(current.Key, out var incoming)) continue;

            defaults[index] = current with
            {
                CostAmount = incoming.CostAmount,
                SaleAmount = incoming.SaleAmount,
            };
        }

        return defaults;
    }

    private static IReadOnlyCollection<LtlChargeItemDto> DefaultLtlChargeItems(string commercialProfile)
    {
        var isNvocc = string.Equals(commercialProfile, "Nvocc", StringComparison.OrdinalIgnoreCase);
        return new LtlChargeItemDto[]
        {
            new("dua", "DUA", "CustomsCharge", "PerDocument", "origin_charges", 50m, 60m, true),
            new("duca-t", "DUCA-T", "Documentation", "PerDocument", "international_freight", 30m, isNvocc ? 30m : 35m, true),
            new("stuffing", "Stuffing", "OriginCharge", "PerChargeableCbm", "origin_charges", 550m / 60m, 10m, true),
            new("carta-porte", "Carta Porte", "Documentation", "PerDocument", "international_freight", 0m, isNvocc ? 35m : 45m, true),
            new("manejos", "Manejos", "AgentCharge", "PerShipment", "origin_charges", 0m, isNvocc ? 25m : 45m, true),
            new("seguro", "Seguro", "Insurance", "PerShipment", "origin_charges", null, null, false),
            new("recolecta", "Recolecta", "OriginCharge", "PerShipment", "pickup_origin", null, null, false),
            new("reembarque", "Reembarque", "Other", "PerShipment", "origin_charges", null, null, false),
            new("inspeccion", "Inspección", "CustomsCharge", "PerShipment", "origin_charges", null, null, false),
            new("tramite-aduanas-destino", "Trámite Aduanas Destino", "CustomsCharge", "PerShipment", "destination_charges", null, null, false),
            new("entrega-destino", "Entrega en Destino", "InlandTransport", "PerShipment", "delivery_destination", null, null, false),
            new("otros", "Otros", "Other", "PerShipment", "destination_charges", null, null, false),
            new("duca-f", "DUCA-F", "Documentation", "PerDocument", "international_freight", null, null, false),
            new("impuesto-exportacion", "Impuesto Exportación", "CustomsCharge", "PerShipment", "origin_charges", null, null, false),
            new("recepcion-destino", "Recepción en Destino", "DestinationCharge", "PerShipment", "destination_charges", null, null, false),
        };
    }

    private static IReadOnlyCollection<LtlChargeItemDto> ReadLtlChargeItems(
        DbDataReader reader,
        int index,
        string shipmentMode,
        string commercialProfile
    )
    {
        if (!string.Equals(shipmentMode, "Ltl", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<LtlChargeItemDto>();

        if (!reader.IsDBNull(index))
        {
            try
            {
                var values = JsonSerializer.Deserialize<LtlChargeItemDto[]>(reader.GetString(index));
                if (values is { Length: > 0 })
                    return NormalizeLtlChargeItems(values, shipmentMode, commercialProfile);
            }
            catch (JsonException)
            {
                // Fall back to the canonical LTL matrix below.
            }
        }

        return DefaultLtlChargeItems(commercialProfile);
    }

    private static string NormalizeCurrencyBusinessCode(string? code, string? name)
    {
        foreach (var candidate in new[] { code, name })
        {
            var value = candidate?.Trim();
            if (!string.IsNullOrWhiteSpace(value)
                && value.Length == 3
                && value.All(char.IsLetter))
            {
                return value.ToUpperInvariant();
            }
        }

        return (name ?? code ?? "USD").Trim();
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
    DateTime? ValidTo,
    string CommercialProfile = "General",
    IReadOnlyCollection<string>? ApplicableEquipmentClasses = null,
    decimal? CostPerCbm = null,
    decimal? WeightKgPerCbm = null,
    decimal? DuaCost = null,
    decimal? DucaTCost = null,
    decimal? StuffingCostPerCbm = null,
    decimal? StuffingSalePerCbm = null,
    decimal? PanamaCostSurchargePerCbm = null,
    IReadOnlyCollection<Guid>? ApplicableOriginIds = null,
    IReadOnlyCollection<Guid>? ApplicableDestinationIds = null,
    IReadOnlyCollection<LtlChargeItemDto>? LtlChargeItems = null
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
    bool IsActive = true,
    string? CommercialProfile = null,
    IReadOnlyCollection<string>? ApplicableEquipmentClasses = null,
    decimal? CostPerCbm = null,
    decimal? WeightKgPerCbm = null,
    decimal? DuaCost = null,
    decimal? DucaTCost = null,
    decimal? StuffingCostPerCbm = null,
    decimal? StuffingSalePerCbm = null,
    decimal? PanamaCostSurchargePerCbm = null,
    IReadOnlyCollection<Guid>? ApplicableOriginIds = null,
    IReadOnlyCollection<Guid>? ApplicableDestinationIds = null,
    IReadOnlyCollection<LtlChargeItemDto>? LtlChargeItems = null
);

public sealed record LtlChargeItemDto(
    string Key,
    string Name,
    string CostDetailType,
    string ChargeBasis,
    string Section,
    decimal? CostAmount,
    decimal? SaleAmount,
    bool IsFlat
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
