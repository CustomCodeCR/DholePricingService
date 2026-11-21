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

        // On own LTL Panama -> Costa Rica the wizard uses a POL at CFZ and a
        // Costa Rican POE (the destination warehouse). The four land masters,
        // however, are stored against Panama POE -> Costa Rica POD. Resolve the
        // physical leg by its port NAME in the corresponding catalog group;
        // never reinterpret the commercial POE for unrelated shipments.
        var originPol = context.ShipmentMode?.ToString() == "Ltl" && context.PolId.HasValue
            ? await configCatalog.GetActiveByIdAsync(context.PolId.Value, cancellationToken)
            : null;
        var destinationPod = originPol is not null && context.PodId.HasValue
            ? await configCatalog.GetActiveByIdAsync(context.PodId.Value, cancellationToken)
            : null;
        var usePanamaLandLeg = originPol is not null && destinationPod is not null
            && IsPanamaOrigin(originPol.Name) && IsCostaRicaDestination(destinationPod.Name)
            && context.PoeId.HasValue && context.AgentId.HasValue;

        var applicable = catalog
            .Where(cost =>
            {
                selections.TryGetValue(cost.Id, out var selection);
                if (GetCostsForSelectQueryHandler.IsApplicableToContext(cost, context, selection))
                    return true;
                if (!usePanamaLandLeg || !IsRequiredPanamaLtlCharge(cost.Name))
                    return false;
                // Resolve only this quoted land leg. The real POE/POD relations
                // must still match agent, services, incoterm and LTL mode.
                var mappedOrigin = cost.Poes?.FirstOrDefault(port =>
                    SamePort(port.Name, originPol!.Name));
                if (mappedOrigin is null) return false;
                return GetCostsForSelectQueryHandler.IsApplicableToContext(
                    cost, context with { PoeId = mappedOrigin.Id }, selection);
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

    internal static bool IsRequiredPanamaLtlCharge(string name)
    {
        var n = NormalizePort(name);
        return (n.Contains("flete") && n.Contains("terrestre") && n.Contains("panama") && n.Contains("costa rica"))
            || (n.Contains("bunker") && n.Contains("panama") && n.Contains("costa rica"))
            || (n.Contains("gastos") && n.Contains("panama") && n.Contains("costa rica"))
            || (n.Contains("custodia") && n.Contains("marchamo"));
    }

    private static bool IsPanamaOrigin(string name) =>
        NormalizePort(name) is var n
        && (n.Contains("panama") || n.Contains("colon") || n.Contains("balboa")
            || n.Contains("cristobal") || n.Contains("rodman") || n.Contains("manzanillo"));

    private static bool IsCostaRicaDestination(string name) =>
        NormalizePort(name) is var n && (n.Contains("costa rica") || n.Contains("san jose"));

    private static bool SamePort(string left, string right) =>
        NormalizePort(left) == NormalizePort(right);

    private static string NormalizePort(string value)
    {
        var decomposed = value.Normalize(System.Text.NormalizationForm.FormD);
        return new string(decomposed
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ')
            .ToArray()).Replace("  ", " ").Trim();
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
        // A contextual projection without cargo dimensions must not masquerade
        // as a zero-priced tier. The raw unpriced master stays in Catalog only.
        if (!weightKg.HasValue && !packages.HasValue
            && !containerTypeId.HasValue && !storageDays.HasValue)
            return source.Where(cost => string.IsNullOrWhiteSpace(cost.PricingRulesJson)
                && (cost.CostAmount != 0m || cost.SaleAmount != 0m
                    || string.Equals(cost.CostType, "Variable", StringComparison.OrdinalIgnoreCase))).ToArray();
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
            // Zero-valued Variable templates (Recolecta/Entrega) are billable editable
            // choices, and a matched zero-valued tier (Gastos Panamá 1-500 kg)
            // is a VALID band, not an absent price. Only hide unconfigured
            // 0/0 Fixed/Optional templates with no pricing rules.
            if (amountCost == 0m && amountSale == 0m
                && string.IsNullOrWhiteSpace(cost.PricingRulesJson)
                && !string.Equals(cost.CostType, "Variable", StringComparison.OrdinalIgnoreCase))
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
