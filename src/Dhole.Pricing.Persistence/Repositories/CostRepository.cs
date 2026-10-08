using System.Data;
using System.Data.Common;
using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Costs.Response;
using Dhole.Pricing.Domain.Costs.Entities;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.Repositories;

public sealed class CostRepository(ServiceDbContext dbContext)
    : EfRepository<Cost, Guid>(dbContext),
        ICostRepository
{
    public Task<Cost?> GetByIdWithIncotermsAsync(
        Guid id,
        CancellationToken cancellationToken = default
    ) => dbContext.Costs
        .Include(x => x.Incoterms)
        .Include(x => x.Services)
        .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<bool> ExistsByNameAsync(
        string name,
        CostType costType,
        CostDetailType costDetailType,
        Guid? portId,
        CostPortRole? portRole,
        Guid? polId,
        Guid? poeId,
        Guid? podId,
        Guid? carrierId = null,
        Guid? agentId = null,
        int shipmentModeMask = 0,
        ChargeBasis chargeBasis = ChargeBasis.PerShipment,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default
    )
    {
        var value = name.Trim().ToLowerInvariant();

        return dbContext.Costs.AnyAsync(
            x =>
                x.Name.ToLower() == value
                && x.CostType == costType
                && x.CostDetailType == costDetailType
                && x.PortId == portId
                && x.PortRole == portRole
                && x.PolId == polId
                && x.PoeId == poeId
                && x.PodId == podId
                && x.CarrierId == carrierId
                && x.AgentId == agentId
                && x.ShipmentModeMask == shipmentModeMask
                && x.ChargeBasis == chargeBasis
                && !x.IsDeleted
                && (!excludeId.HasValue || x.Id != excludeId.Value),
            cancellationToken
        );
    }

    public async Task<IReadOnlyCollection<Cost>> GetActiveCostsAsync(
        CostType? costType = null,
        CostDetailType? costDetailType = null,
        Guid? carrierId = null,
        Guid? agentId = null,
        Guid? portId = null,
        CostPortRole? portRole = null,
        Guid? currencyId = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = ApplyFilters(
            dbContext.Costs.AsNoTracking().Include(x => x.Incoterms).Include(x => x.Services).Where(x => !x.IsDeleted && x.IsActive),
            search: null,
            costType,
            costDetailType,
            carrierId,
            agentId,
            portId,
            portRole,
            currencyId,
            isActive: true
        );

        return await query
            .OrderBy(x => x.CostType)
            .ThenBy(x => x.CostDetailType)
            .ThenBy(x => x.CarrierName)
            .ThenBy(x => x.AgentName)
            .ThenBy(x => x.PortRole)
            .ThenBy(x => x.PortName)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<CostDto>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        IReadOnlyCollection<CostType>? costTypes = null,
        IReadOnlyCollection<CostDetailType>? costDetailTypes = null,
        IReadOnlyCollection<Guid>? carrierIds = null,
        IReadOnlyCollection<Guid>? agentIds = null,
        IReadOnlyCollection<Guid>? portIds = null,
        IReadOnlyCollection<CostPortRole>? portRoles = null,
        IReadOnlyCollection<Guid>? currencyIds = null,
        IReadOnlyCollection<bool>? activeStates = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = ApplyFilters(
            dbContext.Costs.AsNoTracking().Where(x => !x.IsDeleted),
            search,
            costType: null,
            costDetailType: null,
            carrierId: null,
            agentId: null,
            portId: null,
            portRole: null,
            currencyId: null,
            isActive: null
        );

        var selectionMatches = await GetMultiSelectionFilterMatchesAsync(
            carrierIds,
            agentIds,
            portIds,
            cancellationToken
        );

        query = ApplyMultiFilters(
            query,
            costTypes,
            costDetailTypes,
            carrierIds,
            agentIds,
            portIds,
            portRoles,
            currencyIds,
            activeStates,
            selectionMatches.CarrierCostIds,
            selectionMatches.AgentCostIds,
            selectionMatches.PortCostIds
        );

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(x => x.CostType)
            .ThenBy(x => x.CostDetailType)
            .ThenBy(x => x.CarrierName)
            .ThenBy(x => x.AgentName)
            .ThenBy(x => x.PortRole)
            .ThenBy(x => x.PortName)
            .ThenBy(x => x.Name)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(x => new CostDto(
                x.Id,
                x.Name,
                x.CostType.ToString(),
                x.CostDetailType.ToString(),
                x.CarrierId,
                x.CarrierName,
                x.CarrierCode,
                x.AgentId,
                x.AgentName,
                x.AgentCode,
                x.PortId,
                x.PortName,
                x.PortCode,
                x.PortRole.ToString(),
                x.CurrencyId,
                x.CurrencyName,
                x.CurrencyCode,
                x.CostAmount,
                x.SaleAmount,
                x.UtilityAmount,
                x.Notes,
                x.IsAccountant,
                x.IsActive,
                x.Incoterms
                    .OrderBy(i => i.IncotermName)
                    .Select(i => new CostIncotermDto(i.IncotermId, i.IncotermName, i.IncotermCode))
                    .ToList(),
                x.PolId,
                x.PolName,
                x.PolCode,
                x.PoeId,
                x.PoeName,
                x.PoeCode,
                x.PodId,
                x.PodName,
                x.PodCode,
                x.ShipmentMode.HasValue ? x.ShipmentMode.Value.ToString() : null,
                x.ChargeBasis.ToString(),
                x.MinimumCostAmount,
                x.MinimumSaleAmount,
                x.KgPerCbm,
                x.Services
                    .OrderBy(s => s.ServiceName)
                    .Select(s => new CostServiceDto(s.ServiceId, s.ServiceName, s.ServiceCode))
                    .ToList(),
                null,
                null,
                null,
                null,
                null,
                x.OperationalConditions
            ))
            .ToListAsync(cancellationToken);

        items = await AttachShipmentModesAsync(items, cancellationToken);
        items = await AttachMultiSelectionsAsync(items, cancellationToken);

        return PagedResult<CostDto>.Create(items, page.PageNumber, page.PageSize, total);
    }

    public async Task<IReadOnlyCollection<CostSelectDto>> GetForSelectAsync(
        string? search = null,
        CostType? costType = null,
        CostDetailType? costDetailType = null,
        Guid? carrierId = null,
        Guid? agentId = null,
        Guid? portId = null,
        CostPortRole? portRole = null,
        Guid? currencyId = null,
        bool? isActive = true,
        CancellationToken cancellationToken = default
    )
    {
        var query = ApplyFilters(
            dbContext.Costs.AsNoTracking().Where(x => !x.IsDeleted),
            search,
            costType,
            costDetailType,
            carrierId,
            agentId,
            portId,
            portRole,
            currencyId,
            isActive
        );

        var items = await query
            .OrderBy(x => x.CostType)
            .ThenBy(x => x.CostDetailType)
            .ThenBy(x => x.CarrierName)
            .ThenBy(x => x.AgentName)
            .ThenBy(x => x.PortRole)
            .ThenBy(x => x.PortName)
            .ThenBy(x => x.Name)
            .Select(x => new CostSelectDto(
                x.Id,
                x.Name,
                x.CostType.ToString(),
                x.CostDetailType.ToString(),
                x.CarrierId,
                x.CarrierName,
                x.CarrierCode,
                x.AgentId,
                x.AgentName,
                x.AgentCode,
                x.PortId,
                x.PortName,
                x.PortCode,
                x.PortRole.ToString(),
                x.CurrencyId,
                x.CurrencyName,
                x.CurrencyCode,
                x.CostAmount,
                x.SaleAmount,
                x.UtilityAmount,
                x.Notes,
                x.IsAccountant,
                x.Incoterms
                    .OrderBy(i => i.IncotermName)
                    .Select(i => new CostIncotermDto(i.IncotermId, i.IncotermName, i.IncotermCode))
                    .ToList(),
                x.PolId,
                x.PolName,
                x.PolCode,
                x.PoeId,
                x.PoeName,
                x.PoeCode,
                x.PodId,
                x.PodName,
                x.PodCode,
                x.ShipmentMode.HasValue ? x.ShipmentMode.Value.ToString() : null,
                x.ChargeBasis.ToString(),
                x.MinimumCostAmount,
                x.MinimumSaleAmount,
                x.KgPerCbm,
                x.Services
                    .OrderBy(s => s.ServiceName)
                    .Select(s => new CostServiceDto(s.ServiceId, s.ServiceName, s.ServiceCode))
                    .ToList(),
                null,
                null,
                null,
                null,
                null,
                x.OperationalConditions
            ))
            .ToListAsync(cancellationToken);

        return await AttachShipmentModesAsync(items, cancellationToken);
    }

    private async Task<List<CostDto>> AttachShipmentModesAsync(
        List<CostDto> items,
        CancellationToken cancellationToken
    )
    {
        if (items.Count == 0)
            return items;

        var ids = items.Select(item => item.Id).ToArray();
        var states = await dbContext.Costs
            .AsNoTracking()
            .Where(cost => ids.Contains(cost.Id))
            .Select(cost => new { cost.Id, cost.ShipmentModeMask, cost.ShipmentMode })
            .ToDictionaryAsync(cost => cost.Id, cancellationToken);

        return items
            .Select(item => states.TryGetValue(item.Id, out var state)
                ? item with { ShipmentModes = ResolveShipmentModes(state.ShipmentModeMask, state.ShipmentMode) }
                : item)
            .ToList();
    }

    private async Task<IReadOnlyCollection<CostSelectDto>> AttachShipmentModesAsync(
        List<CostSelectDto> items,
        CancellationToken cancellationToken
    )
    {
        if (items.Count == 0)
            return items;

        var ids = items.Select(item => item.Id).ToArray();
        var states = await dbContext.Costs
            .AsNoTracking()
            .Where(cost => ids.Contains(cost.Id))
            .Select(cost => new { cost.Id, cost.ShipmentModeMask, cost.ShipmentMode })
            .ToDictionaryAsync(cost => cost.Id, cancellationToken);

        return items
            .Select(item => states.TryGetValue(item.Id, out var state)
                ? item with { ShipmentModes = ResolveShipmentModes(state.ShipmentModeMask, state.ShipmentMode) }
                : item)
            .ToArray();
    }

    private static IReadOnlyCollection<string> ResolveShipmentModes(
        int shipmentModeMask,
        ShipmentMode? legacyShipmentMode
    )
    {
        if (shipmentModeMask == 0)
            return legacyShipmentMode.HasValue ? [legacyShipmentMode.Value.ToString()] : [];

        return Enum.GetValues<ShipmentMode>()
            .Where(mode => (shipmentModeMask & (1 << ((int)mode - 1))) != 0)
            .Select(mode => mode.ToString())
            .ToArray();
    }

    private static IQueryable<Cost> ApplyFilters(
        IQueryable<Cost> query,
        string? search,
        CostType? costType,
        CostDetailType? costDetailType,
        Guid? carrierId,
        Guid? agentId,
        Guid? portId,
        CostPortRole? portRole,
        Guid? currencyId,
        bool? isActive
    )
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLowerInvariant();

            query = query.Where(x =>
                x.Name.ToLower().Contains(value)
                || x.CostType.ToString().ToLower().Contains(value)
                || x.CostDetailType.ToString().ToLower().Contains(value)
                || (x.CarrierName ?? string.Empty).ToLower().Contains(value)
                || (x.CarrierCode ?? string.Empty).ToLower().Contains(value)
                || (x.AgentName ?? string.Empty).ToLower().Contains(value)
                || (x.AgentCode ?? string.Empty).ToLower().Contains(value)
                || (x.PortName ?? string.Empty).ToLower().Contains(value)
                || (x.PortCode ?? string.Empty).ToLower().Contains(value)
                || (x.PortRole.HasValue
                    && x.PortRole.Value.ToString().ToLower().Contains(value))
                || (x.PolName ?? string.Empty).ToLower().Contains(value)
                || (x.PolCode ?? string.Empty).ToLower().Contains(value)
                || (x.PoeName ?? string.Empty).ToLower().Contains(value)
                || (x.PoeCode ?? string.Empty).ToLower().Contains(value)
                || (x.PodName ?? string.Empty).ToLower().Contains(value)
                || (x.PodCode ?? string.Empty).ToLower().Contains(value)
                || x.Incoterms.Any(i => i.IncotermName.ToLower().Contains(value) || i.IncotermCode.ToLower().Contains(value))
                || x.Services.Any(s => s.ServiceName.ToLower().Contains(value) || s.ServiceCode.ToLower().Contains(value))
                || x.CurrencyName.ToLower().Contains(value)
                || x.CurrencyCode.ToLower().Contains(value)
                || (x.Notes ?? string.Empty).ToLower().Contains(value)
            );
        }

        if (costType.HasValue)
        {
            query = query.Where(x => x.CostType == costType.Value);
        }

        if (costDetailType.HasValue)
        {
            query = query.Where(x => x.CostDetailType == costDetailType.Value);
        }

        if (carrierId.HasValue)
        {
            query = query.Where(x => x.CarrierId == carrierId.Value);
        }

        if (agentId.HasValue)
        {
            query = query.Where(x => x.AgentId == agentId.Value);
        }

        if (portId.HasValue)
        {
            query = query.Where(x =>
                x.PortId == portId.Value
                || x.PolId == portId.Value
                || x.PoeId == portId.Value
                || x.PodId == portId.Value
            );
        }

        if (portRole.HasValue)
        {
            query = portRole.Value switch
            {
                CostPortRole.Pol => query.Where(x => x.PortRole == CostPortRole.Pol || x.PolId.HasValue),
                CostPortRole.Poe => query.Where(x => x.PortRole == CostPortRole.Poe || x.PoeId.HasValue),
                CostPortRole.Pod => query.Where(x => x.PortRole == CostPortRole.Pod || x.PodId.HasValue),
                _ => query.Where(x => x.PortRole == portRole.Value),
            };
        }

        if (currencyId.HasValue)
        {
            query = query.Where(x => x.CurrencyId == currencyId.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(x => x.IsActive == isActive.Value);
        }

        return query;
    }

    private static IQueryable<Cost> ApplyMultiFilters(
        IQueryable<Cost> query,
        IReadOnlyCollection<CostType>? costTypes,
        IReadOnlyCollection<CostDetailType>? costDetailTypes,
        IReadOnlyCollection<Guid>? carrierIds,
        IReadOnlyCollection<Guid>? agentIds,
        IReadOnlyCollection<Guid>? portIds,
        IReadOnlyCollection<CostPortRole>? portRoles,
        IReadOnlyCollection<Guid>? currencyIds,
        IReadOnlyCollection<bool>? activeStates,
        IReadOnlyCollection<Guid>? selectedCarrierCostIds,
        IReadOnlyCollection<Guid>? selectedAgentCostIds,
        IReadOnlyCollection<Guid>? selectedPortCostIds
    )
    {
        if (costTypes is { Count: > 0 })
        {
            var values = costTypes.Distinct().ToArray();
            query = query.Where(x => values.Contains(x.CostType));
        }

        if (costDetailTypes is { Count: > 0 })
        {
            var values = costDetailTypes.Distinct().ToArray();
            query = query.Where(x => values.Contains(x.CostDetailType));
        }

        if (carrierIds is { Count: > 0 })
        {
            var values = carrierIds.Distinct().ToArray();
            var selectedCostIds = selectedCarrierCostIds?.Distinct().ToArray() ?? [];
            query = query.Where(x =>
                (x.CarrierId.HasValue && values.Contains(x.CarrierId.Value))
                || selectedCostIds.Contains(x.Id)
            );
        }

        if (agentIds is { Count: > 0 })
        {
            var values = agentIds.Distinct().ToArray();
            var selectedCostIds = selectedAgentCostIds?.Distinct().ToArray() ?? [];
            query = query.Where(x =>
                (x.AgentId.HasValue && values.Contains(x.AgentId.Value))
                || selectedCostIds.Contains(x.Id)
            );
        }

        if (portIds is { Count: > 0 })
        {
            var values = portIds.Distinct().ToArray();
            var selectedCostIds = selectedPortCostIds?.Distinct().ToArray() ?? [];
            query = query.Where(x =>
                (x.PortId.HasValue && values.Contains(x.PortId.Value))
                || (x.PolId.HasValue && values.Contains(x.PolId.Value))
                || (x.PoeId.HasValue && values.Contains(x.PoeId.Value))
                || (x.PodId.HasValue && values.Contains(x.PodId.Value))
                || selectedCostIds.Contains(x.Id)
            );
        }

        if (portRoles is { Count: > 0 })
        {
            var values = portRoles.Distinct().ToArray();
            var includeAny = values.Contains(CostPortRole.Any);
            var includePol = values.Contains(CostPortRole.Pol);
            var includePoe = values.Contains(CostPortRole.Poe);
            var includePod = values.Contains(CostPortRole.Pod);

            query = query.Where(x =>
                (includeAny && x.PortRole == CostPortRole.Any)
                || (includePol && (x.PortRole == CostPortRole.Pol || x.PolId.HasValue))
                || (includePoe && (x.PortRole == CostPortRole.Poe || x.PoeId.HasValue))
                || (includePod && (x.PortRole == CostPortRole.Pod || x.PodId.HasValue))
            );
        }

        if (currencyIds is { Count: > 0 })
        {
            var values = currencyIds.Distinct().ToArray();
            query = query.Where(x => values.Contains(x.CurrencyId));
        }

        if (activeStates is { Count: > 0 })
        {
            var values = activeStates.Distinct().ToArray();
            if (values.Length == 1)
            {
                var active = values[0];
                query = query.Where(x => x.IsActive == active);
            }
        }

        return query;
    }

    private async Task<MultiSelectionFilterMatches> GetMultiSelectionFilterMatchesAsync(
        IReadOnlyCollection<Guid>? carrierIds,
        IReadOnlyCollection<Guid>? agentIds,
        IReadOnlyCollection<Guid>? portIds,
        CancellationToken cancellationToken
    )
    {
        var normalizedCarrierIds = NormalizeIds(carrierIds);
        var normalizedAgentIds = NormalizeIds(agentIds);
        var normalizedPortIds = NormalizeIds(portIds);

        if (
            normalizedCarrierIds.Length == 0
            && normalizedAgentIds.Length == 0
            && normalizedPortIds.Length == 0
        )
        {
            return MultiSelectionFilterMatches.Empty;
        }

        var connection = dbContext.Database.GetDbConnection();
        var closeWhenDone = connection.State != ConnectionState.Open;
        if (closeWhenDone)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var carrierCostIds = await ReadPartySelectionCostIdsAsync(
                connection,
                "Carrier",
                normalizedCarrierIds,
                cancellationToken
            );
            var agentCostIds = await ReadPartySelectionCostIdsAsync(
                connection,
                "Agent",
                normalizedAgentIds,
                cancellationToken
            );
            var portCostIds = await ReadPortSelectionCostIdsAsync(
                connection,
                normalizedPortIds,
                cancellationToken
            );

            return new MultiSelectionFilterMatches(
                carrierCostIds,
                agentCostIds,
                portCostIds
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

    private async Task<List<CostDto>> AttachMultiSelectionsAsync(
        List<CostDto> items,
        CancellationToken cancellationToken
    )
    {
        if (items.Count == 0)
            return items;

        var ids = items.Select(item => item.Id).Distinct().ToArray();
        var connection = dbContext.Database.GetDbConnection();
        var closeWhenDone = connection.State != ConnectionState.Open;
        if (closeWhenDone)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            var parameterNames = AddGuidParameters(command, "cost", ids);
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

            var selections = new Dictionary<Guid, SelectionAccumulator>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var costId = reader.GetGuid(0);
                var role = reader.GetString(1);
                var selectionId = reader.GetGuid(2);

                if (!selections.TryGetValue(costId, out var selection))
                {
                    selection = new SelectionAccumulator();
                    selections[costId] = selection;
                }

                switch (role.ToLowerInvariant())
                {
                    case "pol":
                        selection.PolIds.Add(selectionId);
                        break;
                    case "poe":
                        selection.PoeIds.Add(selectionId);
                        break;
                    case "pod":
                        selection.PodIds.Add(selectionId);
                        break;
                    case "carrier":
                        selection.CarrierIds.Add(selectionId);
                        break;
                    case "agent":
                        selection.AgentIds.Add(selectionId);
                        break;
                }
            }

            return items
                .Select(item =>
                {
                    selections.TryGetValue(item.Id, out var selection);

                    return item with
                    {
                        Pols = BuildRelations(
                            selection?.PolIds,
                            item.PolId,
                            item.PolName,
                            item.PolCode
                        ),
                        Poes = BuildRelations(
                            selection?.PoeIds,
                            item.PoeId,
                            item.PoeName,
                            item.PoeCode
                        ),
                        Pods = BuildRelations(
                            selection?.PodIds,
                            item.PodId,
                            item.PodName,
                            item.PodCode
                        ),
                        Carriers = BuildRelations(
                            selection?.CarrierIds,
                            item.CarrierId,
                            item.CarrierName,
                            item.CarrierCode
                        ),
                        Agents = BuildRelations(
                            selection?.AgentIds,
                            item.AgentId,
                            item.AgentName,
                            item.AgentCode
                        ),
                    };
                })
                .ToList();
        }
        finally
        {
            if (closeWhenDone)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static IReadOnlyCollection<CostRelationDto> BuildRelations(
        IReadOnlyCollection<Guid>? selectedIds,
        Guid? legacyId,
        string? legacyName,
        string? legacyCode
    )
    {
        var ids = selectedIds is { Count: > 0 }
            ? selectedIds.Where(id => id != Guid.Empty).Distinct().ToArray()
            : legacyId.HasValue && legacyId.Value != Guid.Empty
                ? [legacyId.Value]
                : [];

        return ids
            .Select(id =>
                legacyId == id
                    ? new CostRelationDto(
                        id,
                        string.IsNullOrWhiteSpace(legacyName) ? id.ToString() : legacyName.Trim(),
                        legacyCode?.Trim() ?? string.Empty
                    )
                    : new CostRelationDto(id, id.ToString(), string.Empty)
            )
            .ToArray();
    }

    private static async Task<Guid[]> ReadPartySelectionCostIdsAsync(
        DbConnection connection,
        string partyType,
        IReadOnlyCollection<Guid> partyIds,
        CancellationToken cancellationToken
    )
    {
        if (partyIds.Count == 0)
            return [];

        await using var command = connection.CreateCommand();
        var parameterNames = AddGuidParameters(command, "party", partyIds);
        var partyTypeParameter = command.CreateParameter();
        partyTypeParameter.ParameterName = "@partyType";
        partyTypeParameter.DbType = DbType.String;
        partyTypeParameter.Value = partyType;
        command.Parameters.Add(partyTypeParameter);

        command.CommandText = $"""
            SELECT DISTINCT cost_id
            FROM pricing."CostPartySelections"
            WHERE party_type = @partyType
              AND party_id IN ({string.Join(", ", parameterNames)})
            """;

        return await ReadGuidColumnAsync(command, cancellationToken);
    }

    private static async Task<Guid[]> ReadPortSelectionCostIdsAsync(
        DbConnection connection,
        IReadOnlyCollection<Guid> portIds,
        CancellationToken cancellationToken
    )
    {
        if (portIds.Count == 0)
            return [];

        await using var command = connection.CreateCommand();
        var parameterNames = AddGuidParameters(command, "port", portIds);
        command.CommandText = $"""
            SELECT DISTINCT cost_id
            FROM pricing."CostRoutePortSelections"
            WHERE port_id IN ({string.Join(", ", parameterNames)})
            """;

        return await ReadGuidColumnAsync(command, cancellationToken);
    }

    private static async Task<Guid[]> ReadGuidColumnAsync(
        DbCommand command,
        CancellationToken cancellationToken
    )
    {
        var values = new HashSet<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetGuid(0));
        }

        return values.ToArray();
    }

    private static string[] AddGuidParameters(
        DbCommand command,
        string prefix,
        IReadOnlyCollection<Guid> ids
    )
    {
        var values = ids.Where(id => id != Guid.Empty).Distinct().ToArray();
        var names = new string[values.Length];

        for (var index = 0; index < values.Length; index++)
        {
            var parameterName = $"@{prefix}{index}";
            names[index] = parameterName;
            var parameter = command.CreateParameter();
            parameter.ParameterName = parameterName;
            parameter.DbType = DbType.Guid;
            parameter.Value = values[index];
            command.Parameters.Add(parameter);
        }

        return names;
    }

    private static Guid[] NormalizeIds(IReadOnlyCollection<Guid>? ids) =>
        ids is null
            ? []
            : ids.Where(id => id != Guid.Empty).Distinct().ToArray();

    private sealed record MultiSelectionFilterMatches(
        IReadOnlyCollection<Guid> CarrierCostIds,
        IReadOnlyCollection<Guid> AgentCostIds,
        IReadOnlyCollection<Guid> PortCostIds
    )
    {
        public static MultiSelectionFilterMatches Empty { get; } = new([], [], []);
    }

    private sealed class SelectionAccumulator
    {
        public HashSet<Guid> PolIds { get; } = [];
        public HashSet<Guid> PoeIds { get; } = [];
        public HashSet<Guid> PodIds { get; } = [];
        public HashSet<Guid> CarrierIds { get; } = [];
        public HashSet<Guid> AgentIds { get; } = [];
    }
}
