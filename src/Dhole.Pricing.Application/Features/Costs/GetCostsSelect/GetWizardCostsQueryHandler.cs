using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Contracts.Costs.Response;
using Dhole.Pricing.Domain.Costs.Entities;

namespace Dhole.Pricing.Application.Features.Costs.GetCostsForSelect;

/// <summary>
/// Loads one complete active cost snapshot and projects both catalog and contextual
/// results in memory. This avoids two database/catalog hydration requests per P7
/// navigation while preserving the existing /select route filtering behavior.
/// </summary>
public sealed class GetWizardCostsQueryHandler(
    ICostRepository costs,
    ICostCacheService cache,
    ICostRoutePortSelectionStore routePorts,
    IImportFclRateRepository importRates,
    IPricingConfigCatalogClient configCatalog
) : IQueryHandler<GetWizardCostsQuery, Result<CostWizardBundleDto>>
{
    public async Task<Result<CostWizardBundleDto>> HandleAsync(
        GetWizardCostsQuery request,
        CancellationToken cancellationToken = default
    )
    {
        var context = new GetCostsForSelectQuery(
            CarrierId: request.CarrierId,
            AgentId: request.AgentId,
            PolId: request.PolId,
            PoeId: request.PoeId,
            PodId: request.PodId,
            IncotermId: request.IncotermId,
            ShipmentMode: request.ShipmentMode,
            ServiceIds: request.ServiceIds,
            ImportRateId: request.ImportRateId,
            ApplicableToContext: true,
            IsActive: true
        );

        // A selected imported rate can contain the real POE behind the
        // synthetic "Multimodal Via Panamá" destination.
        if (context.ImportRateId.HasValue)
        {
            var imported = await importRates.GetByIdAsync(context.ImportRateId.Value, cancellationToken);
            if (imported is not null && !imported.IsDeleted)
            {
                context = context with
                {
                    PoeId = imported.PoeId != Guid.Empty ? imported.PoeId : context.PoeId,
                    CarrierId = context.CarrierId ?? imported.CarrierId,
                    AgentId = context.AgentId ?? imported.AgentId,
                };
            }
        }

        var cached = await cache.GetCostsSelectAsync(cancellationToken);
        var active = cached ?? await costs.GetForSelectAsync(
            null, null, null, null, null, null, null, null, true, cancellationToken
        );

        var selections = await routePorts.GetManyAsync(
            active.Select(cost => cost.Id).ToArray(),
            cancellationToken
        );
        // Hydrate relations once for BOTH collections so that multi-POE, POD,
        // carrier and agent selections are consistent within this response.
        var catalog = await GetCostsForSelectQueryHandler.HydrateRelationListsAsync(
            active, selections, configCatalog, cancellationToken
        );
        if (cached is null)
        {
            await cache.SetCostsSelectAsync(catalog, cancellationToken: cancellationToken);
        }

        var applicable = catalog
            .Where(cost =>
            {
                selections.TryGetValue(cost.Id, out var selection);
                return GetCostsForSelectQueryHandler.IsApplicableToContext(cost, context, selection);
            })
            .OrderByDescending(GetCostsForSelectQueryHandler.CostSpecificity)
            .ThenBy(cost => cost.CostType)
            .ThenBy(cost => cost.CostDetailType)
            .ThenBy(cost => cost.Name)
            .ThenBy(cost => cost.Id)
            .ToArray();

        var orderedCatalog = catalog
            .OrderBy(cost => cost.CostType)
            .ThenBy(cost => cost.CostDetailType)
            .ThenBy(cost => cost.Name)
            .ThenBy(cost => cost.Id)
            .ToArray();

        // Catalog is the raw master snapshot; applicable charges carry effective
        // prices for the requested cargo. Never persist calculated values here.
        var priced = CostWizardTierProjection.ResolveApplicable(
            applicable, request.WeightKg, request.Packages,
            request.ContainerTypeId, request.StorageDays);
        return Result.Success(new CostWizardBundleDto(orderedCatalog, priced));
    }
}


/// <summary>Resolve wizard tier amounts without mutating the master cost catalog
/// or any saved quotation. An unmatched tier is not a zero-priced charge.</summary>
public static class CostWizardTierProjection
{
    public static IReadOnlyCollection<CostSelectDto> ResolveApplicable(
        IEnumerable<CostSelectDto> source,
        decimal? weightKg, int? packages, Guid? containerTypeId, int? storageDays)
    {
        // Older callers receive the original catalog data and PricingRulesJson.
        if (!weightKg.HasValue && !packages.HasValue
            && !containerTypeId.HasValue && !storageDays.HasValue)
            return source.ToArray();
        var priced = new List<CostSelectDto>();
        foreach (var cost in source)
        {
            if (!CostPricingRules.TryResolve(
                cost.PricingRulesJson,
                weightKg is > 0 ? weightKg : null,
                packages is > 0 ? packages : null,
                containerTypeId,
                cost.CostAmount, cost.SaleAmount,
                out var amountCost, out var amountSale,
                storageDays is > 0 ? storageDays : null))
                continue;
            priced.Add(cost with
            {
                CostAmount = amountCost,
                SaleAmount = amountSale,
                UtilityAmount = amountSale - amountCost
            });
        }
        return priced;
    }
}
