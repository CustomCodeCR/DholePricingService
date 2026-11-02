using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Dhole.Pricing.Api.Endpoints;

public static class CostRoutePortEndpoints
{
    private const int MaximumSelectionsPerRole = 100;

    public static IEndpointRouteBuilder MapCostRoutePortEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/costs")
            .WithTags("Costs")
            .RequireAuthorization();

        group.MapGet("/{costId:guid}/route-ports", GetAsync)
            .RequireScope(PricingScopeNames.CostView);

        group.MapPut("/{costId:guid}/route-ports", ReplaceAsync)
            .RequireScope(PricingScopeNames.CostUpdate);
        group.MapGet("/payees", GetPayeesAsync)
            .RequireScope(PricingScopeNames.CostView);
        group.MapGet("/{costId:guid}/payee", GetPayeeAsync)
            .RequireScope(PricingScopeNames.CostView);
        group.MapPut("/{costId:guid}/payee", ReplacePayeeAsync)
            .RequireScope(PricingScopeNames.CostUpdate);

        return app;
    }

    private static async Task<IResult> GetAsync(
        Guid costId,
        ICostRepository costs,
        ICostRoutePortSelectionStore routePorts,
        CancellationToken cancellationToken
    )
    {
        var cost = await costs.GetByIdWithIncotermsAsync(costId, cancellationToken);
        if (cost is null)
            return Results.NotFound();

        return Results.Ok(await routePorts.GetAsync(costId, cancellationToken));
    }

    private static async Task<IResult> ReplaceAsync(
        Guid costId,
        CostRoutePortSelectionRequest request,
        ICostRepository costs,
        ICostRoutePortSelectionStore routePorts,
        ICostCacheService cache,
        CancellationToken cancellationToken
    )
    {
        var cost = await costs.GetByIdWithIncotermsAsync(costId, cancellationToken);
        if (cost is null)
            return Results.NotFound();

        var polIds = Normalize(request.PolIds);
        var poeIds = Normalize(request.PoeIds);
        var podIds = Normalize(request.PodIds);
        var carrierIds = Normalize(request.CarrierIds);
        var agentIds = Normalize(request.AgentIds);

        if (
            polIds.Length > MaximumSelectionsPerRole
            || poeIds.Length > MaximumSelectionsPerRole
            || podIds.Length > MaximumSelectionsPerRole
            || carrierIds.Length > MaximumSelectionsPerRole
            || agentIds.Length > MaximumSelectionsPerRole
        )
        {
            return Results.BadRequest(new
            {
                code = "Pricing.CostSelectionsLimitExceeded",
                message = $"Cada relación permite un máximo de {MaximumSelectionsPerRole} opciones.",
            });
        }

        if (carrierIds.Length > 0 && agentIds.Length > 0)
        {
            return Results.BadRequest(new
            {
                code = "Pricing.CostPartySelectionConflict",
                message = "Un costo no puede asociarse simultáneamente a navieras y agentes.",
            });
        }

        await routePorts.ReplaceAsync(
            costId,
            polIds,
            poeIds,
            podIds,
            carrierIds,
            agentIds,
            cancellationToken
        );
        await cache.RemoveCostCacheAsync(costId, cancellationToken);
        await cache.RemoveCostsSelectAsync(cancellationToken);

        return Results.Ok(await routePorts.GetAsync(costId, cancellationToken));
    }

    // Operational association (naviera/agente) and actual payable party
    // are independent; no carrier/agent filters change with this snapshot.
    private static async Task<IResult> GetPayeesAsync(
        Guid[]? costId, ServiceDbContext db, CancellationToken ct)
    {
        var ids = (costId ?? []).Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length > 100)
            return Results.BadRequest(new { code = "Pricing.CostPayeeLookupLimit" });
        if (ids.Length == 0)
            return Results.Ok(new Dictionary<Guid, CostSupplierPayeeSnapshot>());
        var connection = db.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            var names = new List<string>();
            for (var index = 0; index < ids.Length; index++)
            {
                var name = "@id" + index;
                names.Add(name);
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.DbType = DbType.Guid;
                parameter.Value = ids[index];
                command.Parameters.Add(parameter);
            }
            command.CommandText = $"""
                SELECT cost_id, supplier_id, supplier_name, supplier_code, supplier_type
                FROM pricing."CostSupplierPayees"
                WHERE cost_id IN ({string.Join(", ", names)})
                """;
            var results = new Dictionary<Guid, CostSupplierPayeeSnapshot>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                results[reader.GetGuid(0)] = new CostSupplierPayeeSnapshot(
                    reader.GetGuid(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4));
            return Results.Ok(results);
        }
        finally { if (closeConnection) await connection.CloseAsync(); }
    }

    private static async Task<IResult> GetPayeeAsync(
        Guid costId, ICostRepository costs, ServiceDbContext db, CancellationToken ct)
    {
        if (await costs.GetByIdWithIncotermsAsync(costId, ct) is null)
            return Results.NotFound();
        var connection = db.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT supplier_id, supplier_name, supplier_code, supplier_type
                FROM pricing."CostSupplierPayees" WHERE cost_id = @costId
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@costId";
            parameter.DbType = DbType.Guid;
            parameter.Value = costId;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return Results.Ok(new CostSupplierPayeeSnapshot(null, null, null, null));
            return Results.Ok(new CostSupplierPayeeSnapshot(reader.GetGuid(0),
                reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        finally { if (closeConnection) await connection.CloseAsync(); }
    }

    private static async Task<IResult> ReplacePayeeAsync(
        Guid costId, CostSupplierPayeeRequest request, ICostRepository costs,
        ServiceDbContext db, CancellationToken ct)
    {
        if (await costs.GetByIdWithIncotermsAsync(costId, ct) is null)
            return Results.NotFound();
        if (request.SupplierId is null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM pricing.\"CostSupplierPayees\" WHERE cost_id = {costId}", ct);
            return Results.Ok(new CostSupplierPayeeSnapshot(null, null, null, null));
        }
        var name = request.SupplierName?.Trim();
        var code = request.SupplierCode?.Trim();
        var type = request.SupplierType?.Trim();
        if (request.SupplierId == Guid.Empty || string.IsNullOrWhiteSpace(name) ||
            name.Length > 220 || code?.Length > 100 || type?.Length > 60)
            return Results.BadRequest(new { code = "Pricing.InvalidSrmPayee",
                message = "Seleccione un proveedor SRM válido." });
        // Retain the original payee name/code even if its SRM profile changes.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO pricing."CostSupplierPayees"
                (cost_id, supplier_id, supplier_name, supplier_code, supplier_type, updated_at_utc)
            VALUES ({costId}, {request.SupplierId.Value}, {name}, {code}, {type}, now())
            ON CONFLICT (cost_id) DO UPDATE SET
                supplier_id = EXCLUDED.supplier_id,
                supplier_name = EXCLUDED.supplier_name,
                supplier_code = EXCLUDED.supplier_code,
                supplier_type = EXCLUDED.supplier_type,
                updated_at_utc = now()
            """, ct);
        return Results.Ok(new CostSupplierPayeeSnapshot(request.SupplierId, name, code, type));
    }

    private static Guid[] Normalize(IReadOnlyCollection<Guid>? ids) =>
        ids is null
            ? []
            : ids.Where(id => id != Guid.Empty).Distinct().ToArray();
}

public sealed record CostRoutePortSelectionRequest(
    IReadOnlyCollection<Guid>? PolIds,
    IReadOnlyCollection<Guid>? PoeIds,
    IReadOnlyCollection<Guid>? PodIds,
    IReadOnlyCollection<Guid>? CarrierIds = null,
    IReadOnlyCollection<Guid>? AgentIds = null
);
public sealed record CostSupplierPayeeRequest(
    Guid? SupplierId, string? SupplierName, string? SupplierCode, string? SupplierType);
public sealed record CostSupplierPayeeSnapshot(
    Guid? SupplierId, string? SupplierName, string? SupplierCode, string? SupplierType);
