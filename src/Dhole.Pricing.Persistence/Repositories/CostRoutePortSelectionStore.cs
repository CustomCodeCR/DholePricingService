using System.Data;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.Repositories;

public sealed class CostRoutePortSelectionStore(ServiceDbContext dbContext)
    : ICostRoutePortSelectionStore
{
    public async Task<CostRoutePortSelectionSet> GetAsync(
        Guid costId,
        CancellationToken cancellationToken = default
    )
    {
        var items = await GetManyAsync([costId], cancellationToken);
        return items.TryGetValue(costId, out var selection)
            ? selection
            : CostRoutePortSelectionSet.Empty;
    }

    public async Task<IReadOnlyDictionary<Guid, CostRoutePortSelectionSet>> GetManyAsync(
        IReadOnlyCollection<Guid> costIds,
        CancellationToken cancellationToken = default
    )
    {
        var ids = costIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, CostRoutePortSelectionSet>();
        }

        var connection = dbContext.Database.GetDbConnection();
        var closeWhenDone = connection.State != ConnectionState.Open;
        if (closeWhenDone)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            var parameterNames = new string[ids.Length];
            for (var index = 0; index < ids.Length; index++)
            {
                var parameterName = $"@cost{index}";
                parameterNames[index] = parameterName;
                var parameter = command.CreateParameter();
                parameter.ParameterName = parameterName;
                parameter.DbType = DbType.Guid;
                parameter.Value = ids[index];
                command.Parameters.Add(parameter);
            }

            var inClause = string.Join(", ", parameterNames);
            command.CommandText = $"""
                SELECT cost_id, role, port_id AS selection_id
                FROM pricing."CostRoutePortSelections"
                WHERE cost_id IN ({inClause})

                UNION ALL

                SELECT cost_id, party_type AS role, party_id AS selection_id
                FROM pricing."CostPartySelections"
                WHERE cost_id IN ({inClause})
                """;

            var accumulators = new Dictionary<Guid, SelectionAccumulator>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var costId = reader.GetGuid(0);
                var role = reader.GetString(1);
                var selectionId = reader.GetGuid(2);

                if (!accumulators.TryGetValue(costId, out var accumulator))
                {
                    accumulator = new SelectionAccumulator();
                    accumulators[costId] = accumulator;
                }

                switch (role.ToLowerInvariant())
                {
                    case "pol":
                        accumulator.PolIds.Add(selectionId);
                        break;
                    case "poe":
                        accumulator.PoeIds.Add(selectionId);
                        break;
                    case "pod":
                        accumulator.PodIds.Add(selectionId);
                        break;
                    case "carrier":
                        accumulator.CarrierIds.Add(selectionId);
                        break;
                    case "agent":
                        accumulator.AgentIds.Add(selectionId);
                        break;
                }
            }

            return accumulators.ToDictionary(
                pair => pair.Key,
                pair => new CostRoutePortSelectionSet(
                    pair.Value.PolIds.ToArray(),
                    pair.Value.PoeIds.ToArray(),
                    pair.Value.PodIds.ToArray(),
                    pair.Value.CarrierIds.ToArray(),
                    pair.Value.AgentIds.ToArray()
                )
            );
        }
        finally
        {
            if (closeWhenDone)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task ReplaceAsync(
        Guid costId,
        IReadOnlyCollection<Guid> polIds,
        IReadOnlyCollection<Guid> poeIds,
        IReadOnlyCollection<Guid> podIds,
        IReadOnlyCollection<Guid> carrierIds,
        IReadOnlyCollection<Guid> agentIds,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedPolIds = Normalize(polIds);
        var normalizedPoeIds = Normalize(poeIds);
        var normalizedPodIds = Normalize(podIds);
        var normalizedCarrierIds = Normalize(carrierIds);
        var normalizedAgentIds = Normalize(agentIds);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM pricing.\"CostRoutePortSelections\" WHERE cost_id = {costId}",
            cancellationToken
        );
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM pricing.\"CostPartySelections\" WHERE cost_id = {costId}",
            cancellationToken
        );

        await InsertPortAsync(costId, "Pol", normalizedPolIds, cancellationToken);
        await InsertPortAsync(costId, "Poe", normalizedPoeIds, cancellationToken);
        await InsertPortAsync(costId, "Pod", normalizedPodIds, cancellationToken);
        await InsertPartyAsync(costId, "Carrier", normalizedCarrierIds, cancellationToken);
        await InsertPartyAsync(costId, "Agent", normalizedAgentIds, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task InsertPortAsync(
        Guid costId,
        string role,
        IReadOnlyCollection<Guid> portIds,
        CancellationToken cancellationToken
    )
    {
        foreach (var portId in portIds)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO pricing."CostRoutePortSelections" (cost_id, role, port_id)
                VALUES ({costId}, {role}, {portId})
                ON CONFLICT DO NOTHING
                """,
                cancellationToken
            );
        }
    }

    private async Task InsertPartyAsync(
        Guid costId,
        string partyType,
        IReadOnlyCollection<Guid> partyIds,
        CancellationToken cancellationToken
    )
    {
        foreach (var partyId in partyIds)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO pricing."CostPartySelections" (cost_id, party_type, party_id)
                VALUES ({costId}, {partyType}, {partyId})
                ON CONFLICT DO NOTHING
                """,
                cancellationToken
            );
        }
    }

    private static Guid[] Normalize(IReadOnlyCollection<Guid> ids) =>
        ids.Where(id => id != Guid.Empty).Distinct().ToArray();

    private sealed class SelectionAccumulator
    {
        public HashSet<Guid> PolIds { get; } = [];
        public HashSet<Guid> PoeIds { get; } = [];
        public HashSet<Guid> PodIds { get; } = [];
        public HashSet<Guid> CarrierIds { get; } = [];
        public HashSet<Guid> AgentIds { get; } = [];
    }
}