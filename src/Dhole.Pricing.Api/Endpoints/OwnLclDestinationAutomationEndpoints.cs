using System.Data;
using System.Data.Common;
using System.Text.Json;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Application.Features.Costs.GetCostsForSelect;
using Dhole.Pricing.Contracts.Costs.Response;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class OwnLclDestinationAutomationEndpoints
{
    private const decimal DefaultMaximumCbm = 50m;
    private const decimal DefaultPanamaToCostaRicaCost = 2140m;
    private const decimal DefaultBunkerCost = 476m;
    private const decimal DefaultCostaRicaTransferBaseCbm = 95m;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapOwnLclDestinationAutomationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/own-lcl-automation")
            .WithTags("Own LCL destination automation")
            .RequireAuthorization();

        group.MapGet("/destination-preview", PreviewAsync)
            .RequireAnyScope(PricingConstants.Scopes.OwnLclConsolidationView, PricingConstants.Scopes.RateView);
        group.MapGet("/pricing-lines-preview", PreviewPricingLines)
            .RequireAnyScope(PricingConstants.Scopes.OwnLclConsolidationView, PricingConstants.Scopes.RateView, PricingConstants.Scopes.OwnLclConsolidationCreate);
        group.MapPost("/consolidations", CreateAsync)
            .RequireIdempotency()
            .RequireScope(PricingConstants.Scopes.OwnLclConsolidationCreate);
        group.MapPut("/consolidations/{id:guid}", UpdateAsync)
            .RequireScope(PricingConstants.Scopes.OwnLclConsolidationUpdate);
        group.MapGet("/consolidations/{id:guid}", GetAutomationAsync)
            .RequireAnyScope(PricingConstants.Scopes.OwnLclConsolidationView, PricingConstants.Scopes.RateView);

        return app;
    }

    private static IResult PreviewPricingLines(string? polCode, string? polName, decimal? destinationCostPerCbm)
    {
        var destination = Math.Max(0m, destinationCostPerCbm ?? 0m);
        return Results.Ok(OwnLclPricingLineCatalog.ForOrigin(polCode, polName)
            .Select(line => new
            {
                lineKey = line.LineKey,
                scope = line.Scope,
                name = line.Name,
                chargeBasis = line.ChargeBasis,
                costUnit = line.LineKey switch
                {
                    "PA_DESTINATION_CHARGE" => destination,
                    "CA_TRANSSHIPMENT" => destination + 9m,
                    _ => line.DefaultCostUnit ?? 0m,
                },
                saleUnit = line.DefaultSaleUnit,
                calculationBaseCbm = line.LineKey.StartsWith("CA_INLAND_", StringComparison.OrdinalIgnoreCase)
                    ? (decimal?)70m : null,
            }).ToArray());
    }

    private static async Task<IResult> PreviewAsync(
        string? carrierCode,
        string? carrierName,
        string? arrivalPortCode,
        decimal? maximumCbm,
        bool? includeEmptyReturn,
        string? containerCode,
        decimal? bunkerCost,
        Guid? carrierId,
        Guid? polId,
        Guid? poeId,
        Guid? containerTypeId,
        IQueryDispatcher dispatcher,
        CancellationToken ct)
    {
        var result = await ResolveAsync(
            carrierCode,
            carrierName,
            arrivalPortCode,
            maximumCbm is > 0 ? maximumCbm.Value : DefaultMaximumCbm,
            includeEmptyReturn,
            containerCode,
            bunkerCost,
            carrierId, polId, poeId, containerTypeId,
            dispatcher, ct);

        return result is null
            ? MissingMatrixOrSelectionResult(carrierCode, carrierName, arrivalPortCode, "Seleccione naviera y POE para calcular los cargos de destino.")
            : Results.Ok(result);
    }

    private static async Task<IResult> CreateAsync(
        AutomaticOwnLclConsolidationRequest request,
        ServiceDbContext db,
        IQueryDispatcher dispatcher,
        CancellationToken ct)
    {
        var validation = Validate(request);
        if (validation is not null) return Results.BadRequest(validation);

        var maximumCbm = request.MaximumCbm is > 0 ? request.MaximumCbm.Value : DefaultMaximumCbm;
        var isMiami = OwnLclPricingLineCatalog.IsMiamiOrigin(request.PolCode, request.PolName);
        var profile = isMiami
            ? BuildMiamiProfile(request, maximumCbm)
            : await ResolveAsync(
                request.CarrierCode,
                request.CarrierName,
                request.PanamaArrivalPortCode,
                maximumCbm,
                request.IncludeEmptyReturn,
                request.ContainerCode,
                request.BunkerCost,
                request.CarrierId, request.PolId, request.PanamaArrivalPortId, request.ContainerId,
                dispatcher, ct);

        if (profile is null)
            return MissingMatrixOrSelectionResult(
                request.CarrierCode, request.CarrierName, request.PanamaArrivalPortCode,
                "Seleccione naviera y POE para calcular el consolidado.");

        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        int nextNumber;
        if (request.ConsolidationNumber is > 0)
        {
            nextNumber = request.ConsolidationNumber.Value;
            if (await ConsolidationNumberExistsAsync(connection, tx, nextNumber, null, ct))
            {
                await tx.RollbackAsync(ct);
                return Results.Conflict(new
                {
                    code = "Pricing.OwnLclConsolidationNumberAlreadyExists",
                    message = $"Ya existe un consolidado con el número {nextNumber}.",
                });
            }
        }
        else
        {
            await using var sequence = connection.CreateCommand();
            sequence.Transaction = tx;
            sequence.CommandText = "SELECT GREATEST(COALESCE(MAX(consolidation_number), 47) + 1, 48) FROM pricing.\"OwnLclConsolidations\";";
            nextNumber = Convert.ToInt32(await sequence.ExecuteScalarAsync(ct));
        }

        var id = Guid.NewGuid();
        var consolidationName = string.IsNullOrWhiteSpace(request.Name)
            ? OwnLclPricingLineCatalog.DefaultConsolidationName(request.PolCode, request.PolName, nextNumber)
            : request.Name.Trim();
        var version = BuildMatrixVersion(
            OwnLclPricingLineCatalog.MatrixVersionPrefix(request.PolCode, request.PolName),
            nextNumber,
            null);
        var snapshot = JsonSerializer.Serialize(profile, JsonOptions);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = """
                INSERT INTO pricing."OwnLclConsolidations"
                    (id, consolidation_number, name, booking, etd,
                     carrier_id, carrier_name, carrier_code,
                     container_id, container_name, container_code,
                     pol_id, pol_name, pol_code,
                     ocean_freight, maximum_cbm,
                     carrier_destination_cost_total, panama_to_cr_cost, bunker_cost, cr_transfer_base_cbm,
                     freight_profit_per_cbm,
                     panama_arrival_port_id, panama_arrival_port_name, panama_arrival_port_code,
                     destination_profile_code, destination_profile_version, destination_charge_snapshot_json,
                     include_empty_return, matrix_version, status, is_active, created_at_utc)
                VALUES
                    (@id, @number, @name, @booking, @etd,
                     @carrier_id, @carrier_name, @carrier_code,
                     @container_id, @container_name, @container_code,
                     @pol_id, @pol_name, @pol_code,
                     @ocean_freight, @maximum_cbm,
                     @destination_total, @panama_to_cr, @bunker, @cr_base,
                     @freight_profit_per_cbm,
                     @arrival_port_id, @arrival_port_name, @arrival_port_code,
                     @profile_code, @profile_version, CAST(@snapshot AS jsonb),
                     @include_empty_return, @matrix_version, 'Draft', TRUE, now());
                """;

            Add(command, "id", id);
            Add(command, "number", nextNumber);
            Add(command, "name", consolidationName);
            Add(command, "booking", NullIfBlank(request.Booking));
            Add(command, "etd", request.Etd);
            Add(command, "carrier_id", request.CarrierId);
            Add(command, "carrier_name", NullIfBlank(request.CarrierName));
            Add(command, "carrier_code", Normalize(request.CarrierCode));
            Add(command, "container_id", request.ContainerId);
            Add(command, "container_name", NullIfBlank(request.ContainerName));
            Add(command, "container_code", Normalize(request.ContainerCode));
            Add(command, "pol_id", request.PolId);
            Add(command, "pol_name", NullIfBlank(request.PolName));
            Add(command, "pol_code", Normalize(request.PolCode));
            Add(command, "ocean_freight", request.OceanFreight);
            Add(command, "maximum_cbm", maximumCbm);
            Add(command, "destination_total", profile.TotalCost);
            Add(command, "panama_to_cr", profile.CostaRicaTransfer.PanamaToCostaRica);
            Add(command, "bunker", profile.CostaRicaTransfer.Bunker);
            Add(command, "cr_base", profile.CostaRicaTransfer.BaseCbm);
            Add(command, "freight_profit_per_cbm", Math.Max(0m, request.FreightProfitPerCbm ?? 5.69m));
            Add(command, "arrival_port_id", request.PanamaArrivalPortId);
            Add(command, "arrival_port_name", NullIfBlank(request.PanamaArrivalPortName));
            Add(command, "arrival_port_code", Normalize(request.PanamaArrivalPortCode));
            Add(command, "profile_code", profile.ProfileCode);
            Add(command, "profile_version", profile.Version);
            Add(command, "snapshot", snapshot);
            Add(command, "include_empty_return", profile.IncludeEmptyReturn);
            Add(command, "matrix_version", version);
            await command.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return Results.Created(
            $"/api/pricing/own-lcl-consolidations/{id}",
            new AutomaticOwnLclCreatedResponse(
                id,
                nextNumber,
                consolidationName,
                version,
                profile));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        AutomaticOwnLclConsolidationRequest request,
        ServiceDbContext db,
        IQueryDispatcher dispatcher,
        CancellationToken ct)
    {
        var validation = Validate(request);
        if (validation is not null) return Results.BadRequest(validation);

        var maximumCbm = request.MaximumCbm is > 0 ? request.MaximumCbm.Value : DefaultMaximumCbm;
        var isMiami = OwnLclPricingLineCatalog.IsMiamiOrigin(request.PolCode, request.PolName);
        var profile = isMiami
            ? BuildMiamiProfile(request, maximumCbm)
            : await ResolveAsync(
                request.CarrierCode,
                request.CarrierName,
                request.PanamaArrivalPortCode,
                maximumCbm,
                request.IncludeEmptyReturn,
                request.ContainerCode,
                request.BunkerCost,
                request.CarrierId, request.PolId, request.PanamaArrivalPortId, request.ContainerId,
                dispatcher, ct);

        if (profile is null)
            return MissingMatrixOrSelectionResult(
                request.CarrierCode, request.CarrierName, request.PanamaArrivalPortCode,
                "Seleccione naviera y POE para calcular el consolidado.");

        var snapshot = JsonSerializer.Serialize(profile, JsonOptions);
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        int currentNumber;
        string currentVersion;
        await using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = tx;
            lookup.CommandText = """
                SELECT consolidation_number, matrix_version
                FROM pricing."OwnLclConsolidations"
                WHERE id=@id AND is_active=TRUE
                LIMIT 1;
                """;
            Add(lookup, "id", id);
            await using var reader = await lookup.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                await tx.RollbackAsync(ct);
                return Results.NotFound();
            }

            currentNumber = reader.GetInt32(0);
            currentVersion = reader.GetString(1);
        }

        var newNumber = request.ConsolidationNumber is > 0
            ? request.ConsolidationNumber.Value
            : currentNumber;
        if (await ConsolidationNumberExistsAsync(connection, tx, newNumber, id, ct))
        {
            await tx.RollbackAsync(ct);
            return Results.Conflict(new
            {
                code = "Pricing.OwnLclConsolidationNumberAlreadyExists",
                message = $"Ya existe otro consolidado con el número {newNumber}.",
            });
        }

        var newVersion = BuildMatrixVersion(
            OwnLclPricingLineCatalog.MatrixVersionPrefix(request.PolCode, request.PolName),
            newNumber,
            currentVersion);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = """
                UPDATE pricing."OwnLclConsolidations"
                SET consolidation_number=@number,
                    booking=@booking,
                    etd=@etd,
                    carrier_id=@carrier_id,
                    carrier_name=@carrier_name,
                    carrier_code=@carrier_code,
                    container_id=@container_id,
                    container_name=@container_name,
                    container_code=@container_code,
                    pol_id=@pol_id,
                    pol_name=@pol_name,
                    pol_code=@pol_code,
                    ocean_freight=@ocean_freight,
                    maximum_cbm=@maximum_cbm,
                    carrier_destination_cost_total=@destination_total,
                    panama_to_cr_cost=@panama_to_cr,
                    bunker_cost=@bunker,
                    cr_transfer_base_cbm=@cr_base,
                    freight_profit_per_cbm=COALESCE(@freight_profit_per_cbm, freight_profit_per_cbm),
                    panama_arrival_port_id=@arrival_port_id,
                    panama_arrival_port_name=@arrival_port_name,
                    panama_arrival_port_code=@arrival_port_code,
                    destination_profile_code=@profile_code,
                    destination_profile_version=@profile_version,
                    destination_charge_snapshot_json=CAST(@snapshot AS jsonb),
                    include_empty_return=@include_empty_return,
                    matrix_version=@matrix_version,
                    updated_at_utc=now()
                WHERE id=@id AND is_active=TRUE;
                """;

            Add(command, "id", id);
            Add(command, "number", newNumber);
            Add(command, "booking", NullIfBlank(request.Booking));
            Add(command, "etd", request.Etd);
            Add(command, "carrier_id", request.CarrierId);
            Add(command, "carrier_name", NullIfBlank(request.CarrierName));
            Add(command, "carrier_code", Normalize(request.CarrierCode));
            Add(command, "container_id", request.ContainerId);
            Add(command, "container_name", NullIfBlank(request.ContainerName));
            Add(command, "container_code", Normalize(request.ContainerCode));
            Add(command, "pol_id", request.PolId);
            Add(command, "pol_name", NullIfBlank(request.PolName));
            Add(command, "pol_code", Normalize(request.PolCode));
            Add(command, "ocean_freight", request.OceanFreight);
            Add(command, "maximum_cbm", maximumCbm);
            Add(command, "destination_total", profile.TotalCost);
            Add(command, "panama_to_cr", profile.CostaRicaTransfer.PanamaToCostaRica);
            Add(command, "bunker", profile.CostaRicaTransfer.Bunker);
            Add(command, "cr_base", profile.CostaRicaTransfer.BaseCbm);
            Add(command, "freight_profit_per_cbm", request.FreightProfitPerCbm.HasValue ? Math.Max(0m, request.FreightProfitPerCbm.Value) : null);
            Add(command, "arrival_port_id", request.PanamaArrivalPortId);
            Add(command, "arrival_port_name", NullIfBlank(request.PanamaArrivalPortName));
            Add(command, "arrival_port_code", Normalize(request.PanamaArrivalPortCode));
            Add(command, "profile_code", profile.ProfileCode);
            Add(command, "profile_version", profile.Version);
            Add(command, "snapshot", snapshot);
            Add(command, "include_empty_return", profile.IncludeEmptyReturn);
            Add(command, "matrix_version", newVersion);

            if (await command.ExecuteNonQueryAsync(ct) == 0)
            {
                await tx.RollbackAsync(ct);
                return Results.NotFound();
            }
        }

        if (newNumber != currentNumber)
        {
            await using var history = connection.CreateCommand();
            history.Transaction = tx;
            history.CommandText = """
                UPDATE pricing."OwnLclHistoricalRates"
                SET consolidation_number=@new_number
                WHERE consolidation_number=@old_number;
                """;
            Add(history, "new_number", newNumber);
            Add(history, "old_number", currentNumber);
            await history.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return Results.Ok(profile);
    }

    private static async Task<IResult> GetAutomationAsync(Guid id, ServiceDbContext db, CancellationToken ct)
    {
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT panama_arrival_port_id, panama_arrival_port_name, panama_arrival_port_code,
                   destination_profile_code, destination_profile_version,
                   destination_charge_snapshot_json::text, include_empty_return
            FROM pricing."OwnLclConsolidations"
            WHERE id=@id AND is_active=TRUE;
            """;
        Add(command, "id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return Results.NotFound();

        return Results.Ok(new
        {
            panamaArrivalPortId = DbGuid(reader, 0),
            panamaArrivalPortName = DbString(reader, 1),
            panamaArrivalPortCode = DbString(reader, 2),
            destinationProfileCode = DbString(reader, 3),
            destinationProfileVersion = DbString(reader, 4),
            destinationProfile = ParseSnapshot(DbString(reader, 5)),
            includeEmptyReturn = !reader.IsDBNull(6) && reader.GetBoolean(6),
        });
    }

    private static AutomaticDestinationProfileDto BuildMiamiProfile(
        AutomaticOwnLclConsolidationRequest request,
        decimal maximumCbm)
    {
        var arrivalCode = Normalize(request.PanamaArrivalPortCode);
        var arrivalName = string.IsNullOrWhiteSpace(request.PanamaArrivalPortName)
            ? arrivalCode
            : request.PanamaArrivalPortName.Trim();

        return new AutomaticDestinationProfileDto(
            "MIAMI-PROJECT",
            "MIA-LIVE",
            "Matriz propia Miami",
            "USD",
            arrivalCode,
            arrivalCode,
            arrivalName,
            request.IncludeEmptyReturn ?? false,
            Array.Empty<AutomaticDestinationChargeDto>(),
            0m,
            0m,
            new CostaRicaTransferDto(0m, 0m, Math.Max(0.01m, maximumCbm)),
            true,
            "Pricing: matriz Miami por consolidado");
    }

    private static string BuildMatrixVersion(string prefix, int consolidationNumber, string? currentVersion)
    {
        var revision = 1;
        if (!string.IsNullOrWhiteSpace(currentVersion))
        {
            var marker = currentVersion.LastIndexOf("-v", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0
                && int.TryParse(currentVersion[(marker + 2)..], out var parsed)
                && parsed > 0)
            {
                revision = parsed;
            }
        }

        return $"{prefix}-{consolidationNumber:000}-v{revision}";
    }

    private static async Task<bool> ConsolidationNumberExistsAsync(
        DbConnection connection,
        DbTransaction transaction,
        int consolidationNumber,
        Guid? excludeId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = excludeId.HasValue
            ? """
              SELECT EXISTS (
                  SELECT 1
                  FROM pricing."OwnLclConsolidations"
                  WHERE consolidation_number=@number AND id<>@id
              );
              """
            : """
              SELECT EXISTS (
                  SELECT 1
                  FROM pricing."OwnLclConsolidations"
                  WHERE consolidation_number=@number
              );
              """;
        Add(command, "number", consolidationNumber);
        if (excludeId.HasValue) Add(command, "id", excludeId.Value);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(ct));
    }

    private static object? Validate(AutomaticOwnLclConsolidationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CarrierCode) && string.IsNullOrWhiteSpace(request.CarrierName))
            return new { code = "Pricing.OwnLclCarrierRequired", message = "Seleccione la naviera." };
        if (string.IsNullOrWhiteSpace(request.Booking))
            return new { code = "Pricing.OwnLclBookingRequired", message = "Ingrese el número de booking." };
        if (!request.Etd.HasValue)
            return new { code = "Pricing.OwnLclEtdRequired", message = "Ingrese el ETD del consolidado." };
        if (string.IsNullOrWhiteSpace(request.ContainerCode) && string.IsNullOrWhiteSpace(request.ContainerName))
            return new { code = "Pricing.OwnLclContainerRequired", message = "Seleccione el tamaño y tipo de contenedor." };
        if (string.IsNullOrWhiteSpace(request.PanamaArrivalPortCode))
            return new { code = "Pricing.OwnLclPanamaArrivalPortRequired", message = "Seleccione el puerto de llegada en Panamá." };
        if (string.IsNullOrWhiteSpace(request.PolCode))
            return new { code = "Pricing.OwnLclPolRequired", message = "Seleccione el POL." };
        if (request.OceanFreight <= 0)
            return new { code = "Pricing.OwnLclOceanFreightRequired", message = "Ingrese el flete marítimo del consolidado." };
        if (request.MaximumCbm is <= 0)
            return new { code = "Pricing.OwnLclMaximumCbmInvalid", message = "La base máxima de CBM debe ser mayor a cero." };
        if (request.FreightProfitPerCbm is < 0)
            return new { code = "Pricing.OwnLclFreightProfitInvalid", message = "La utilidad por CBM no puede ser negativa." };
        if (request.BunkerCost is < 0)
            return new { code = "Pricing.OwnLclBunkerInvalid", message = "El Bunker no puede ser negativo." };
        if (request.ConsolidationNumber is <= 0)
            return new { code = "Pricing.OwnLclConsolidationNumberInvalid", message = "El número del consolidado debe ser mayor a cero." };
        return null;
    }

    private static async Task<AutomaticDestinationProfileDto?> ResolveAsync(
        string? carrierCode, string? carrierName, string? arrivalPortCode,
        decimal maximumCbm, bool? includeEmptyReturn, string? containerCode, decimal? bunkerCost,
        Guid? carrierId, Guid? polId, Guid? poeId, Guid? containerTypeId,
        IQueryDispatcher dispatcher, CancellationToken ct)
    {
        var result = await dispatcher.DispatchAsync(new GetWizardCostsQuery(
            carrierId, null, polId, poeId, null, null, ShipmentMode.Lcl, null,
            ContainerTypeId: containerTypeId), ct);
        if (result.IsFailure) return null;
        return ResolveWizardDestination(result.Value, carrierCode, carrierName, arrivalPortCode,
            maximumCbm, includeEmptyReturn, containerCode, bunkerCost, carrierId, poeId);
    }

    // Both wizard-bundle and the persistence endpoints use this exact projection.
    // Catalog relations are hydrated by the same CQRS query as ordinary quotations.
    internal static AutomaticDestinationProfileDto? ResolveWizardDestination(
        CostWizardBundleDto bundle, string? carrierCode, string? carrierName, string? arrivalPortCode,
        decimal maximumCbm, bool? includeEmptyReturn, string? containerCode, decimal? bunkerCost,
        Guid? carrierId, Guid? poeId)
    {
        var carrierCandidates = new[] { carrierCode, carrierName }
            .Select(NormalizeMatch).Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var portCandidate = NormalizeMatch(arrivalPortCode);
        if (carrierCandidates.Count == 0 || portCandidate.Length == 0) return null;

        var isMaerskBalboa = OwnLclDestinationCostPolicy.IsMaerskBalboaSelection(carrierCandidates, portCandidate);
        bool MatchCarrierValue(string? value) => carrierCandidates.Contains(NormalizeMatch(value))
            || (isMaerskBalboa && OwnLclDestinationCostPolicy.IsMaerskBalboaSelection(new[] { value }, arrivalPortCode));
        bool MatchPortValue(string? value) => NormalizeMatch(value) == portCandidate
            || (isMaerskBalboa && OwnLclDestinationCostPolicy.IsBalboaPort(value));

        bool CarrierMatches(CostSelectDto cost)
        {
            if (cost.Carriers is { Count: > 0 })
                return cost.Carriers.Any(x => x.Id == carrierId || MatchCarrierValue(x.Code) || MatchCarrierValue(x.Name));
            return !cost.CarrierId.HasValue && string.IsNullOrWhiteSpace(cost.CarrierCode) && string.IsNullOrWhiteSpace(cost.CarrierName)
                || (carrierId.HasValue && cost.CarrierId == carrierId)
                || MatchCarrierValue(cost.CarrierCode) || MatchCarrierValue(cost.CarrierName);
        }

        bool PortMatches(CostSelectDto cost)
        {
            if (cost.Poes is { Count: > 0 })
                return cost.Poes.Any(x => x.Id == poeId || MatchPortValue(x.Code) || MatchPortValue(x.Name));
            return (poeId.HasValue && cost.PoeId == poeId)
                || MatchPortValue(cost.PoeCode) || MatchPortValue(cost.PoeName)
                || (string.Equals(cost.PortRole, "Poe", StringComparison.OrdinalIgnoreCase)
                    && ((poeId.HasValue && cost.PortId == poeId) || MatchPortValue(cost.PortCode) || MatchPortValue(cost.PortName)));
        }

        bool IsDestination(CostSelectDto cost) =>
            (string.Equals(cost.CurrencyCode, "USD", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cost.CurrencyName, "USD", StringComparison.OrdinalIgnoreCase))
            && !string.Equals(cost.CostDetailType, "Freight", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(cost.CostDetailType, "OriginCharge", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(cost.CostDetailType, "Insurance", StringComparison.OrdinalIgnoreCase)
            && CarrierMatches(cost) && PortMatches(cost);

        var applicable = bundle.Applicable.Where(IsDestination).ToArray();
        var matches = applicable;
        if (isMaerskBalboa)
        {
            matches = OwnLclDestinationCostPolicy.ResolveMaerskBalboaCharges(
                applicable, cost => cost.Id, cost => cost.Name, cost => cost.CostAmount);
            // These four explicitly approved charges describe the physical
            // container. The live catalog also includes masters configured as FCL.
            // Do not admit any other FCL or generic line into an own LCL project.
            if (matches.Length != OwnLclDestinationCostPolicy.RequiredMaerskBalboaChargeCount)
                matches = OwnLclDestinationCostPolicy.ResolveMaerskBalboaCharges(
                    bundle.Catalog.Where(IsDestination), cost => cost.Id, cost => cost.Name, cost => cost.CostAmount);
            if (matches.Length != OwnLclDestinationCostPolicy.RequiredMaerskBalboaChargeCount)
                return OwnLclDestinationCostPolicy.BuildApprovedMaerskBalboaReference(
                    carrierCandidates, arrivalPortCode, maximumCbm, includeEmptyReturn, bunkerCost);
        }
        if (matches.Length == 0)
            return BuildEmptyMatrixProfile(carrierCode, carrierName, arrivalPortCode, portCandidate,
                maximumCbm, includeEmptyReturn, bunkerCost);
        matches = matches.OrderBy(cost => cost.Name).ToArray();

        var useEmptyReturn = includeEmptyReturn ?? true;
        var compactContainer = NormalizeMatch(containerCode);
        var teuMultiplier = compactContainer.StartsWith("20", StringComparison.Ordinal) ? 1m
            : compactContainer.StartsWith("40", StringComparison.Ordinal)
                || compactContainer.StartsWith("45", StringComparison.Ordinal)
                || compactContainer.StartsWith("48", StringComparison.Ordinal)
                || compactContainer.StartsWith("53", StringComparison.Ordinal)
                ? 2m
                : 1m;

        decimal ProjectAmount(CostSelectDto cost)
        {
            var projected = cost.ChargeBasis switch
            {
                "PerCbm" or "PerChargeableCbm" => cost.CostAmount * maximumCbm,
                "PerTeu" => cost.CostAmount * teuMultiplier,
                _ => cost.CostAmount,
            };
            return Math.Max(projected, cost.MinimumCostAmount ?? 0m);
        }

        bool IsEmptyReturn(CostSelectDto cost)
        {
            var name = NormalizeMatch(cost.Name);
            return name.Contains("EMPTYRETURN", StringComparison.Ordinal)
                || name.Contains("RETIRODEVACIO", StringComparison.Ordinal)
                || name.Contains("RETIROVACIO", StringComparison.Ordinal)
                || name.Contains("VACIOYROLEO", StringComparison.Ordinal);
        }

        var charges = matches
            .Select(cost =>
            {
                var emptyReturn = IsEmptyReturn(cost);
                var optional = string.Equals(cost.CostType, "Optional", StringComparison.OrdinalIgnoreCase) || emptyReturn;
                var included = !emptyReturn || useEmptyReturn;
                return new AutomaticDestinationChargeDto(
                    $"COST-{cost.Id:N}",
                    cost.Name,
                    ProjectAmount(cost),
                    cost.ChargeBasis.ToString(),
                    !optional,
                    optional,
                    included,
                    new[]
                    {
                        cost.CostDetailType.ToString(),
                        cost.CostType.ToString(),
                        cost.ChargeBasis.ToString(),
                    });
            })
            .ToArray();

        var total = charges.Where(charge => charge.Included).Sum(charge => charge.Amount);
        var carrierLabel = matches.Select(cost => cost.CarrierName)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? matches.Select(cost => cost.CarrierCode).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? "Naviera";
        var portLabel = matches.Select(cost => cost.PoeName)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? matches.Select(cost => cost.PortName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? Normalize(arrivalPortCode);

        var profileCode = BuildProfileCode(carrierLabel, portCandidate);

        return new AutomaticDestinationProfileDto(
            profileCode,
            "MATRIX-LIVE",
            $"Matriz de costos · {carrierLabel} · {portLabel}",
            "USD",
            Normalize(arrivalPortCode),
            "CFZ",
            "Colón Free Zone",
            useEmptyReturn,
            charges,
            total,
            total / Math.Max(0.01m, maximumCbm),
            new CostaRicaTransferDto(
                DefaultPanamaToCostaRicaCost,
                bunkerCost ?? DefaultBunkerCost,
                DefaultCostaRicaTransferBaseCbm),
            true,
            "Pricing: costs/wizard-bundle (naviera + POE)");
    }

    private static AutomaticDestinationProfileDto BuildEmptyMatrixProfile(
        string? carrierCode,
        string? carrierName,
        string? arrivalPortCode,
        string portCandidate,
        decimal maximumCbm,
        bool? includeEmptyReturn,
        decimal? bunkerCost)
    {
        var carrierLabel = !string.IsNullOrWhiteSpace(carrierName)
            ? carrierName.Trim()
            : !string.IsNullOrWhiteSpace(carrierCode)
                ? carrierCode.Trim()
                : "Naviera";
        var portLabel = Normalize(arrivalPortCode);
        var useEmptyReturn = includeEmptyReturn ?? true;

        return new AutomaticDestinationProfileDto(
            BuildProfileCode(carrierLabel, portCandidate),
            "MATRIX-EMPTY",
            $"Matriz de costos · {carrierLabel} · {portLabel}",
            "USD",
            portLabel,
            "CFZ",
            "Colón Free Zone",
            useEmptyReturn,
            Array.Empty<AutomaticDestinationChargeDto>(),
            0m,
            0m / Math.Max(0.01m, maximumCbm),
            new CostaRicaTransferDto(
                DefaultPanamaToCostaRicaCost,
                bunkerCost ?? DefaultBunkerCost,
                DefaultCostaRicaTransferBaseCbm),
            true,
            "Pricing: Matriz de costos (sin cargos LCL/Any aplicables)");
    }

    private static IResult MissingMatrixOrSelectionResult(
        string? carrierCode, string? carrierName, string? arrivalPortCode, string selectionMessage)
    {
        if (OwnLclDestinationCostPolicy.IsMaerskBalboaSelection(
                new[] { carrierCode, carrierName }, arrivalPortCode))
        {
            return Results.UnprocessableEntity(new
            {
                code = "Pricing.OwnLclMaerskBalboaCostsIncomplete",
                message = "La matriz Maersk LCL / Balboa debe contener los cuatro cargos autorizados (342 + 80 + 90 + 400). Revisá los registros antes de crear o guardar el consolidado.",
            });
        }

        return Results.BadRequest(new
        {
            code = "Pricing.OwnLclDestinationSelectionRequired",
            message = selectionMessage,
        });
    }

    private static string BuildProfileCode(string carrierLabel, string portCandidate)
    {
        var profileCode = $"MATRIX-{NormalizeMatch(carrierLabel)}-{portCandidate}";
        return profileCode.Length > 90 ? profileCode[..90] : profileCode;
    }

    private static object? ParseSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch (JsonException) { return null; }
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static string NormalizeMatch(string? value)
        => new((value ?? string.Empty)
            .Trim()
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private static object? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = $"@{name}";
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static async Task EnsureOpenAsync(DbConnection connection, CancellationToken ct)
    {
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
    }

    private static Guid? DbGuid(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);

    private static string? DbString(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}

public sealed record AutomaticOwnLclConsolidationRequest(
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
    decimal? MaximumCbm,
    Guid? PanamaArrivalPortId,
    string? PanamaArrivalPortName,
    string PanamaArrivalPortCode,
    bool? IncludeEmptyReturn,
    decimal? BunkerCost = 476m,
    decimal? FreightProfitPerCbm = null,
    string? Name = null,
    int? ConsolidationNumber = null);

public sealed record AutomaticOwnLclCreatedResponse(
    Guid Id,
    int ConsolidationNumber,
    string Name,
    string MatrixVersion,
    AutomaticDestinationProfileDto DestinationProfile);

public sealed record AutomaticDestinationProfileDto(
    string ProfileCode,
    string Version,
    string ProfileName,
    string Currency,
    string ArrivalPortCode,
    string FinalRatePointCode,
    string FinalRatePointName,
    bool IncludeEmptyReturn,
    IReadOnlyCollection<AutomaticDestinationChargeDto> Charges,
    decimal TotalCost,
    decimal CostPerCbm,
    CostaRicaTransferDto CostaRicaTransfer,
    bool CostsEditable,
    string Source);

public sealed record AutomaticDestinationChargeDto(
    string Code,
    string Name,
    decimal Amount,
    string Basis,
    bool Required,
    bool Optional,
    bool Included,
    IReadOnlyCollection<string> Components);

public sealed record CostaRicaTransferDto(
    decimal PanamaToCostaRica,
    decimal Bunker,
    decimal BaseCbm);
