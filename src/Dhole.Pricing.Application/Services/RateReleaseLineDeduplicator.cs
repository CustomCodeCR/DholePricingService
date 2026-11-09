using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.Application.Services;

// A catalog CostId is not sufficient to detect an identical Release charge:
// carrier and agent catalogs can provide two different IDs for the same line.
// Limit this safeguard to Release; repeated Manejos/HBL may be intentional.
public static class RateReleaseLineDeduplicator
{
    private static bool IsRelease(string? name) =>
        string.Equals(name?.Trim(), "Release", StringComparison.OrdinalIgnoreCase);

    private static bool EqualText(string? left, string? right) =>
        string.Equals(left?.Trim() ?? "", right?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);

    public static bool HasEquivalent(
        RateHeader rate,
        string name,
        ChargeBasis basis,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes,
        decimal quantity,
        string? billToClient,
        bool applyDestinationTax,
        decimal destinationTaxRate
    ) =>
        IsRelease(name) && rate.RateDetails.Any(detail =>
            IsRelease(detail.Name)
            && detail.ChargeBasis == basis
            && EqualText(detail.CurrencyCode, currencyCode)
            && detail.CostAmount == costAmount
            && detail.SaleAmount == saleAmount
            && EqualText(detail.Notes, notes)
            && detail.Quantity == quantity
            && EqualText(detail.BillToClient, billToClient)
            && detail.ApplyDestinationTax == applyDestinationTax
            && (!applyDestinationTax || detail.DestinationTaxRate == destinationTaxRate));

    public static void RemoveExactDuplicates(RateHeader rate, Guid? updatedBy)
    {
        var accepted = new List<RateDetail>();

        foreach (var detail in rate.RateDetails.Where(x => IsRelease(x.Name)).ToArray())
        {
            if (accepted.Any(previous =>
                previous.ChargeBasis == detail.ChargeBasis
                && EqualText(previous.CurrencyCode, detail.CurrencyCode)
                && previous.CostAmount == detail.CostAmount
                && previous.SaleAmount == detail.SaleAmount
                && EqualText(previous.Notes, detail.Notes)
                && previous.Quantity == detail.Quantity
                && EqualText(previous.BillToClient, detail.BillToClient)
                && previous.ApplyDestinationTax == detail.ApplyDestinationTax
                && (!detail.ApplyDestinationTax || previous.DestinationTaxRate == detail.DestinationTaxRate)))
            {
                rate.RemoveRateDetail(detail.Id, updatedBy);
            }
            else
            {
                accepted.Add(detail);
            }
        }
    }
}
