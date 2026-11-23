using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.Features.Rates.SetRateStatus;

/// <summary>
/// Immutable, versioned business snapshot. Operations must never mutate the source QUO.
/// A group A/B has exactly one opening, bound to the selected accepted rate.
/// </summary>
public static class AcceptedQuoteApertureEvent
{
    public const string EventName = "pricing.quote.accepted.for-opening.v1";

    public static string GroupNumber(string quoteNumber)
    {
        var normalized = quoteNumber.Trim().ToUpperInvariant();
        var suffix = normalized.LastIndexOf('-');
        if (suffix > 0 && normalized.Length - suffix == 2 &&
            normalized[^1] is >= 'A' and <= 'Z')
            return normalized[..suffix];
        return normalized;
    }

    public static object Capture(RateHeader rate, Guid? acceptedBy, string? observations)
    {
        if (rate.Status != RateStatus.AcceptedByClient)
            throw new InvalidOperationException("Only an accepted QUO can initiate an opening.");
        var number = string.IsNullOrWhiteSpace(rate.QuoNumber) ? rate.RateCode : rate.QuoNumber;
        var groupNumber = GroupNumber(number);
        var isAlternative = !string.Equals(groupNumber, number.Trim().ToUpperInvariant(), StringComparison.Ordinal);
        return new
        {
            schemaVersion = 1,
            quoteId = rate.Id,
            quoteOptionId = isAlternative ? rate.Id : (Guid?)null,
            quoteNumber = number,
            groupQuoteNumber = groupNumber,
            revisionNumber = rate.RevisionNumber,
            acceptedAtUtc = rate.AcceptedAtUtc,
            acceptedBy,
            accountId = rate.ClientAccountId,
            clientName = rate.ClientName,
            executiveName = rate.ExecutiveName,
            modalityCode = rate.ShipmentMode switch
            {
                ShipmentMode.Air or ShipmentMode.AirConsol => "AIR",
                _ => rate.ShipmentMode.ToString().ToUpperInvariant()
            },
            shipmentMode = rate.ShipmentMode.ToString().ToUpperInvariant(),
            operationType = rate.OperationType.ToString().ToUpperInvariant(),
            pol = new { id = rate.PolId, name = rate.PolName, code = rate.PolCode },
            poe = new { id = rate.PoeId, name = rate.PoeName, code = rate.PoeCode },
            pod = new { id = rate.PodId, name = rate.PodName, code = rate.PodCode },
            incoterm = rate.IncotermCode,
            pickup = new { rate.WarehouseId, rate.PickupAddress, rate.PickupLocationsJson, rate.InlandServiceLocationsJson },
            carrier = new { rate.CarrierId, rate.CarrierName, rate.CarrierCode, rate.CarrierRoute, rate.CarrierComments },
            agent = new { rate.AgentId, rate.AgentName, rate.AgentCode },
            cargo = new
            {
                rate.TotalPackages, rate.TotalPallets, rate.TotalWeightKg, rate.TotalVolumeCbm,
                rate.CargoLinesJson, rate.CargoValue
            },
            equipment = rate.RateContainers.Select(x => new
            {
                x.ContainerTypeId, x.ContainerTypeName, x.ContainerTypeCode, x.Quantity
            }).ToArray(),
            services = rate.RateServices.Select(x => new
            {
                x.ServiceId, x.ServiceName, x.ServiceCode
            }).ToArray(),
            costs = rate.RateDetails.Select(x => new
            {
                sourceCostId = x.Id, catalogCostId = x.CostId,
                costCode = x.CostType.ToString(),
                description = x.Name, currencyCode = x.CurrencyCode,
                x.CostAmount, x.SaleAmount, x.Quantity,
                x.Notes, x.BillToClient, x.ApplyDestinationTax, x.DestinationTaxRate,
                chargeBasis = x.ChargeBasis.ToString()
            }).ToArray(),
            terms = new { rate.Includes, rate.SubjectTo, rate.Excludes, rate.TransitTime, rate.FreeDays },
            commercial = new
            {
                rate.CurrencyCode, rate.TotalCostAmount, rate.TotalSaleAmount,
                rate.TotalUtilityAmount, rate.MarginPercentage,
                rate.ExchangeRateApplied
            },
            acceptanceObservations = observations
        };
    }
}
