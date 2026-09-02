using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Api.Endpoints;

public static class OwnLclDestinationAutomationEndpoints
{
    private const string ProfileCatalogSlug = "pricing-own-lcl-destination-profiles";
    private const decimal DefaultMaximumCbm = 50m;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapOwnLclDestinationAutomationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/own-lcl-automation")
            .WithTags("Own LCL destination automation")
            .RequireAuthorization();

        group.MapGet("/destination-preview", PreviewAsync)
            .RequireScope(PricingConstants.Scopes.RateView);
        group.MapPost("/consolidations", CreateAsync)
            .RequireScope(PricingConstants.Scopes.RateCreate);
        group.MapPut("/consolidations/{id:guid}", UpdateAsync)
            .RequireScope(PricingConstants.Scopes.RateUpdate);
        group.MapGet("/consolidations/{id:guid}", GetAutomationAsync)
            .RequireScope(PricingConstants.Scopes.RateView);

        return app;
    }

    private static async Task<IResult> PreviewAsync(
        string? carrierCode,
        string? carrierName,
        string? arrivalPortCode,
        decimal? maximumCbm,
        bool? includeEmptyReturn,
        string? containerCode,
        IPricingConfigCatalogClient config,
        ServiceDbContext db,
        CancellationToken ct)
    {
        var result = await ResolveAsync(
            carrierCode,
            carrierName,
            arrivalPortCode,
            maximumCbm is > 0 ? maximumCbm.Value : DefaultMaximumCbm,
            includeEmptyReturn,
            containerCode,
            config,
            db,
            ct);

        return result is null
            ? Results.NotFound(new
            {
                code = "Pricing.OwnLclDestinationProfileNotConfigured",
                message = "No hay costos activos aplicables en la Matriz de costos para la combinación de naviera y POE seleccionada.",
                carrierCode,
                carrierName,
                arrivalPortCode,
            })
            : Results.Ok(result);
    }

    private static async Task<IResult> CreateAsync(
        AutomaticOwnLclConsolidationRequest request,
        ServiceDbContext db,
        IPricingConfigCatalogClient config,
        CancellationToken ct)
    {
        var validation = Validate(request);
        if (validation is not null) return Results.BadRequest(validation);

        var maximumCbm = request.MaximumCbm is > 0 ? request.MaximumCbm.Value : DefaultMaximumCbm;
        var profile = await ResolveAsync(
            request.CarrierCode,
            request.CarrierName,
            request.PanamaArrivalPortCode,
            maximumCbm,
            request.IncludeEmptyReturn,
            request.ContainerCode,
            config,
            db,
            ct);

        if (profile is null)
            return Results.BadRequest(new
            {
                code = "Pricing.OwnLclDestinationProfileNotConfigured",
                message = "Configure los cargos de esta naviera + POE en la Matriz de costos de Pricing antes de crear el consolidado.",
            });

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
        var version = $"CNCA-{nextNumber:000}-v1";
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
                     @arrival_port_id, @arrival_port_name, @arrival_port_code,
                     @profile_code, @profile_version, CAST(@snapshot AS jsonb),
                     @include_empty_return, @matrix_version, 'Draft', TRUE, now());
                """;

            Add(command, "id", id);
            Add(command, "number", nextNumber);
            Add(command, "name", $"Consolidado {nextNumber}");
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
                $"Consolidado {nextNumber}",
                version,
                profile));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        AutomaticOwnLclConsolidationRequest request,
        ServiceDbContext db,
        IPricingConfigCatalogClient config,
        CancellationToken ct)
    {
        var validation = Validate(request);
        if (validation is not null) return Results.BadRequest(validation);

        var maximumCbm = request.MaximumCbm is > 0 ? request.MaximumCbm.Value : DefaultMaximumCbm;
        var profile = await ResolveAsync(
            request.CarrierCode,
            request.CarrierName,
            request.PanamaArrivalPortCode,
            maximumCbm,
            request.IncludeEmptyReturn,
            request.ContainerCode,
            config,
            db,
            ct);

        if (profile is null)
            return Results.BadRequest(new
            {
                code = "Pricing.OwnLclDestinationProfileNotConfigured",
                message = "No existen costos automáticos para esta naviera + POE en la Matriz de costos. El costo no puede ingresarse manualmente.",
            });

        var snapshot = JsonSerializer.Serialize(profile, JsonOptions);
        await using var connection = db.Database.GetDbConnection();
        await EnsureOpenAsync(connection, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE pricing."OwnLclConsolidations"
            SET booking=@booking,
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
                panama_arrival_port_id=@arrival_port_id,
                panama_arrival_port_name=@arrival_port_name,
                panama_arrival_port_code=@arrival_port_code,
                destination_profile_code=@profile_code,
                destination_profile_version=@profile_version,
                destination_charge_snapshot_json=CAST(@snapshot AS jsonb),
                include_empty_return=@include_empty_return,
                updated_at_utc=now()
            WHERE id=@id AND is_active=TRUE;
            """;

        Add(command, "id", id);
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
        Add(command, "arrival_port_id", request.PanamaArrivalPortId);
        Add(command, "arrival_port_name", NullIfBlank(request.PanamaArrivalPortName));
        Add(command, "arrival_port_code", Normalize(request.PanamaArrivalPortCode));
        Add(command, "profile_code", profile.ProfileCode);
        Add(command, "profile_version", profile.Version);
        Add(command, "snapshot", snapshot);
        Add(command, "include_empty_return", profile.IncludeEmptyReturn);

        return await command.ExecuteNonQueryAsync(ct) == 0 ? Results.NotFound() : Results.Ok(profile);
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

    private static object? Validate(AutomaticOwnLclConsolidationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CarrierCode) && string.IsNullOrWhiteSpace(request.CarrierName))
            return new { code = "Pricing.OwnLclCarrierRequired", message = "Seleccione la naviera." };
        if (string.IsNullOrWhiteSpace(request.PanamaArrivalPortCode))
            return new { code = "Pricing.OwnLclPanamaArrivalPortRequired", message = "Seleccione el puerto de llegada en Panamá." };
        if (string.IsNullOrWhiteSpace(request.PolCode))
            return new { code = "Pricing.OwnLclPolRequired", message = "Seleccione el POL." };
        if (request.OceanFreight <= 0)
            return new { code = "Pricing.OwnLclOceanFreightRequired", message = "Ingrese el flete marítimo del consolidado." };
        if (request.MaximumCbm is <= 0)
            return new { code = "Pricing.OwnLclMaximumCbmInvalid", message = "La base máxima de CBM debe ser mayor a cero." };
        return null;
    }

    private static async Task<AutomaticDestinationProfileDto?> ResolveAsync(
        string? carrierCode,
        string? carrierName,
        string? arrivalPortCode,
        decimal maximumCbm,
        bool? includeEmptyReturn,
        string? containerCode,
        IPricingConfigCatalogClient config,
        ServiceDbContext db,
        CancellationToken ct)
    {
        var carrierCandidates = new[] { carrierCode, carrierName }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeMatch)
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var portCandidate = NormalizeMatch(arrivalPortCode);
        if (carrierCandidates.Count == 0 || portCandidate.Length == 0) return null;

        // Pricing's Cost Matrix is the source of truth for own-LCL destination costs.
        // Config profiles remain only as a backwards-compatible fallback.
        var matrixProfile = await ResolveFromCostMatrixAsync(
            carrierCandidates,
            portCandidate,
            arrivalPortCode,
            maximumCbm,
            includeEmptyReturn,
            containerCode,
            db,
            ct);
        if (matrixProfile is not null) return matrixProfile;

        var items = await config.GetActiveByGroupAsync(ProfileCatalogSlug, ct);
        foreach (var item in items)
        {
            var definition = ParseDefinition(item);
            if (definition is null) continue;
            if (!definition.CarrierAliases.Any(alias => carrierCandidates.Contains(NormalizeMatch(alias)))) continue;
            if (!definition.ArrivalPortAliases.Any(alias => NormalizeMatch(alias) == portCandidate)) continue;

            var useEmptyReturn = includeEmptyReturn ?? definition.DefaultIncludeEmptyReturn;
            var charges = definition.Charges
                .Select(charge =>
                {
                    var included = charge.Required
                        || (string.Equals(charge.Code, "EMPTY_RETURN", StringComparison.OrdinalIgnoreCase)
                            ? useEmptyReturn
                            : charge.DefaultIncluded);
                    return new AutomaticDestinationChargeDto(
                        charge.Code,
                        charge.Name,
                        charge.Amount,
                        charge.Basis,
                        charge.Required,
                        !charge.Required,
                        included,
                        charge.Components);
                })
                .ToArray();
            var total = charges.Where(charge => charge.Included).Sum(charge => charge.Amount);

            return new AutomaticDestinationProfileDto(
                item.Code,
                definition.Version,
                item.Name,
                definition.Currency,
                Normalize(arrivalPortCode),
                definition.FinalRatePointCode,
                definition.FinalRatePointName,
                useEmptyReturn,
                charges,
                total,
                total / Math.Max(0.01m, maximumCbm),
                definition.CostaRicaTransfer,
                false,
                "Config: naviera + puerto de llegada");
        }

        return null;
    }

    private static async Task<AutomaticDestinationProfileDto?> ResolveFromCostMatrixAsync(
        IReadOnlySet<string> carrierCandidates,
        string portCandidate,
        string? arrivalPortCode,
        decimal maximumCbm,
        bool? includeEmptyReturn,
        string? containerCode,
        ServiceDbContext db,
        CancellationToken ct)
    {
        var candidates = await db.Costs
            .AsNoTracking()
            .Where(cost =>
                cost.IsActive
                // Own-LCL only accepts costs configured for every mode (NULL) or explicitly for LCL.
                // FCL, FTL and LTL rows must never leak into an LCL destination profile.
                && (cost.ShipmentMode == null || cost.ShipmentMode == ShipmentMode.Lcl)
                && cost.PolId == null
                && cost.PodId == null
                && cost.CurrencyCode == "USD"
                && cost.CostDetailType != CostDetailType.Freight
                && cost.CostDetailType != CostDetailType.OriginCharge
                && cost.CostDetailType != CostDetailType.Insurance)
            .ToListAsync(ct);

        bool CarrierMatches(Dhole.Pricing.Domain.Costs.Entities.Cost cost)
        {
            var hasCarrierRestriction = cost.CarrierId.HasValue
                || !string.IsNullOrWhiteSpace(cost.CarrierCode)
                || !string.IsNullOrWhiteSpace(cost.CarrierName);
            if (!hasCarrierRestriction) return true;

            return new[] { cost.CarrierCode, cost.CarrierName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(NormalizeMatch)
                .Any(carrierCandidates.Contains);
        }

        bool PortMatches(Dhole.Pricing.Domain.Costs.Entities.Cost cost)
        {
            var structured = new[] { cost.PoeCode, cost.PoeName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(NormalizeMatch)
                .Any(value => value == portCandidate);
            if (structured) return true;

            if (cost.PortRole != CostPortRole.Poe) return false;
            return new[] { cost.PortCode, cost.PortName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(NormalizeMatch)
                .Any(value => value == portCandidate);
        }

        var matches = candidates
            .Where(cost => CarrierMatches(cost) && PortMatches(cost))
            .OrderBy(cost => cost.Name)
            .ToArray();
        if (matches.Length == 0) return null;

        var useEmptyReturn = includeEmptyReturn ?? true;
        var compactContainer = NormalizeMatch(containerCode);
        var teuMultiplier = compactContainer.StartsWith("20", StringComparison.Ordinal) ? 1m
            : compactContainer.StartsWith("40", StringComparison.Ordinal)
                || compactContainer.StartsWith("45", StringComparison.Ordinal)
                || compactContainer.StartsWith("48", StringComparison.Ordinal)
                || compactContainer.StartsWith("53", StringComparison.Ordinal)
                ? 2m
                : 1m;

        decimal ProjectAmount(Dhole.Pricing.Domain.Costs.Entities.Cost cost)
        {
            var projected = cost.ChargeBasis switch
            {
                ChargeBasis.PerCbm or ChargeBasis.PerChargeableCbm => cost.CostAmount * maximumCbm,
                ChargeBasis.PerTeu => cost.CostAmount * teuMultiplier,
                _ => cost.CostAmount,
            };
            return Math.Max(projected, cost.MinimumCostAmount ?? 0m);
        }

        bool IsEmptyReturn(Dhole.Pricing.Domain.Costs.Entities.Cost cost)
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
                var optional = cost.CostType == CostType.Optional || emptyReturn;
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

        var profileCode = $"MATRIX-{NormalizeMatch(carrierLabel)}-{portCandidate}";
        if (profileCode.Length > 90) profileCode = profileCode[..90];

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
            // Existing China -> Central America operational baseline. It remains a separate
            // transfer component while destination charges come from the live Cost Matrix.
            new CostaRicaTransferDto(2140m, 280m, 95m),
            false,
            "Pricing: Matriz de costos (naviera + POE)");
    }

    private static DestinationProfileDefinition? ParseDefinition(PricingConfigCatalogItem item)
    {
        if (string.IsNullOrWhiteSpace(item.MetadataJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(item.MetadataJson);
            var root = doc.RootElement;
            var charges = new List<DestinationChargeDefinition>();
            if (root.TryGetProperty("charges", out var chargeArray) && chargeArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var charge in chargeArray.EnumerateArray())
                {
                    charges.Add(new DestinationChargeDefinition(
                        GetString(charge, "code") ?? string.Empty,
                        GetString(charge, "name") ?? string.Empty,
                        GetDecimal(charge, "amount"),
                        GetString(charge, "basis") ?? "CONTAINER",
                        GetBool(charge, "required", true),
                        GetBool(charge, "defaultIncluded", true),
                        GetStringArray(charge, "components")));
                }
            }

            var cr = root.TryGetProperty("costaRicaTransfer", out var crElement)
                ? new CostaRicaTransferDto(
                    GetDecimal(crElement, "panamaToCostaRica"),
                    GetDecimal(crElement, "bunker"),
                    Math.Max(0.01m, GetDecimal(crElement, "baseCbm", 95m)))
                : new CostaRicaTransferDto(2140m, 280m, 95m);

            return new DestinationProfileDefinition(
                GetString(root, "version") ?? item.Code,
                GetString(root, "currency") ?? "USD",
                GetStringArray(root, "carrierAliases"),
                GetStringArray(root, "arrivalPortAliases"),
                GetString(root, "finalRatePointCode") ?? "CFZ",
                GetString(root, "finalRatePointName") ?? "Colón Free Zone",
                GetBool(root, "defaultIncludeEmptyReturn", true),
                charges,
                cr);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static object? ParseSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch (JsonException) { return null; }
    }

    private static string[] GetStringArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];
        return value.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToArray();
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal GetDecimal(JsonElement element, string property, decimal fallback = 0m)
        => element.TryGetProperty(property, out var value) && value.TryGetDecimal(out var result)
            ? result
            : fallback;

    private static bool GetBool(JsonElement element, string property, bool fallback)
        => element.TryGetProperty(property, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

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

    private sealed record DestinationProfileDefinition(
        string Version,
        string Currency,
        IReadOnlyCollection<string> CarrierAliases,
        IReadOnlyCollection<string> ArrivalPortAliases,
        string FinalRatePointCode,
        string FinalRatePointName,
        bool DefaultIncludeEmptyReturn,
        IReadOnlyCollection<DestinationChargeDefinition> Charges,
        CostaRicaTransferDto CostaRicaTransfer);

    private sealed record DestinationChargeDefinition(
        string Code,
        string Name,
        decimal Amount,
        string Basis,
        bool Required,
        bool DefaultIncluded,
        IReadOnlyCollection<string> Components);
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
    bool? IncludeEmptyReturn);

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