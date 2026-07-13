using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Costs.Entities;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.Application.Services;

public sealed class RateFixedCostSynchronizer(ICostRepository costs) : IRateFixedCostSynchronizer
{
    public async Task SynchronizeAsync(
        RateHeader rate,
        Guid? updatedBy,
        CancellationToken cancellationToken = default
    )
    {
        var existingAmounts = rate
            .RateDetails.Where(x => x.CostId.HasValue && x.CostType == CostType.Fixed)
            .GroupBy(x => x.CostId!.Value)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var detail = group.First();
                    return (detail.CostAmount, detail.SaleAmount);
                }
            );

        rate.RemoveAutomaticFixedDetails(updatedBy);

        var resolvedCosts = new List<Cost>();

        await AddRouteCostsAsync(
            resolvedCosts,
            rate.AgentId,
            rate.CarrierId,
            rate.PolId,
            CostPortRole.Pol,
            cancellationToken
        );

        await AddRouteCostsAsync(
            resolvedCosts,
            rate.AgentId,
            rate.CarrierId,
            rate.PoeId,
            CostPortRole.Poe,
            cancellationToken
        );

        await AddRouteCostsAsync(
            resolvedCosts,
            rate.AgentId,
            rate.CarrierId,
            rate.PodId,
            CostPortRole.Pod,
            cancellationToken
        );

        foreach (var cost in resolvedCosts.GroupBy(x => x.Id).Select(group => group.First()))
        {
            var hasExistingAmount = existingAmounts.TryGetValue(cost.Id, out var existingAmount);
            var costAmount = hasExistingAmount ? existingAmount.CostAmount : cost.CostAmount;
            var saleAmount = cost.AgentId.HasValue
                ? 0m
                : hasExistingAmount
                    ? existingAmount.SaleAmount
                    : cost.SaleAmount;

            rate.AddRateDetail(
                rate.Id,
                cost.Id,
                cost.Name,
                cost.CostDetailType,
                cost.CostType,
                cost.CurrencyId,
                cost.CurrencyName,
                cost.CurrencyCode,
                costAmount,
                saleAmount,
                cost.Notes,
                updatedBy
            );
        }
    }

    private async Task AddRouteCostsAsync(
        ICollection<Cost> destination,
        Guid? agentId,
        Guid? carrierId,
        Guid portId,
        CostPortRole portRole,
        CancellationToken cancellationToken
    )
    {
        if (agentId.HasValue)
        {
            var agentCosts = await costs.GetActiveCostsAsync(
                costType: CostType.Fixed,
                costDetailType: null,
                carrierId: null,
                agentId: agentId.Value,
                portId: portId,
                portRole: portRole,
                currencyId: null,
                cancellationToken: cancellationToken
            );

            foreach (var cost in agentCosts)
            {
                destination.Add(cost);
            }
        }

        if (carrierId.HasValue)
        {
            var carrierCosts = await costs.GetActiveCostsAsync(
                costType: CostType.Fixed,
                costDetailType: null,
                carrierId: carrierId.Value,
                agentId: null,
                portId: portId,
                portRole: portRole,
                currencyId: null,
                cancellationToken: cancellationToken
            );

            foreach (var cost in carrierCosts)
            {
                destination.Add(cost);
            }
        }
    }
}
