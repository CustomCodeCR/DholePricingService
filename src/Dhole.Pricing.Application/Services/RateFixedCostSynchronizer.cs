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
                    return (detail.CostAmount, detail.SaleAmount, detail.Quantity);
                }
            );

        rate.RemoveAutomaticFixedDetails(updatedBy);

        var activeFixedCosts = await costs.GetActiveCostsAsync(
            costType: CostType.Fixed,
            cancellationToken: cancellationToken
        );

        foreach (var cost in activeFixedCosts.Where(cost => MatchesRate(cost, rate)))
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
                cost.IsAccountant ? rate.ContainerQuantity : 1,
                updatedBy
            );
        }
    }

    private static bool MatchesRate(Cost cost, RateHeader rate)
    {
        var matchesAgent = !cost.AgentId.HasValue || cost.AgentId == rate.AgentId;
        var matchesCarrier = !cost.CarrierId.HasValue || cost.CarrierId == rate.CarrierId;

        if (!matchesAgent || !matchesCarrier)
            return false;

        if (!cost.PortId.HasValue)
            return true;

        return cost.PortRole switch
        {
            CostPortRole.Pol => cost.PortId == rate.PolId,
            CostPortRole.Poe => cost.PortId == rate.PoeId,
            CostPortRole.Pod => cost.PortId == rate.PodId,
            CostPortRole.Any =>
                cost.PortId == rate.PolId || cost.PortId == rate.PoeId || cost.PortId == rate.PodId,
            null => cost.PortId == rate.PolId || cost.PortId == rate.PoeId || cost.PortId == rate.PodId,
            _ => false,
        };
    }
}
