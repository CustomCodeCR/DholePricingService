using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.Application.Features.Rates.SetRateStatus;

/// <summary>Same persisted commercial data for live acceptance and historical recovery.</summary>
public static class AcceptedQuoteSnapshot
{
    public static object Build(RateHeader rate, Guid quoteId, Guid? acceptedActor, Guid? historicalActor = null, string? historicalIdtra = null) =>
        new
                {
                    schemaVersion = 1,
                    rateId = rate.Id,
                    quoteId = quoteId,
                    acceptedOptionId = rate.Id,
                    acceptedByUserId = acceptedActor,
                    historicalSourceActorId = historicalActor,
                    historicalIdtra = historicalIdtra,
                    quoteNumber = rate.QuoNumber,
                    commercialRevision = rate.RevisionNumber,
                    acceptedAtUtc = rate.AcceptedAtUtc,
                    crmAccountId = rate.ClientAccountId,
                    crmLeadId = rate.ClientLeadId,
                    clientName = rate.ClientName,
                    commercialExecutiveName = rate.ExecutiveName,
                    shipmentMode = rate.ShipmentMode.ToString(),
                    operationType = rate.OperationType.ToString(),
                    incoterm = rate.IncotermCode,
                    incotermName = rate.IncotermName,
                    pickupAddress = rate.PickupAddress,
                    otherReferences = rate.OtherReferences,
                    transitTime = rate.TransitTime,
                    validFrom = rate.ValidFrom,
                    validTo = rate.ValidTo,
                    freeDays = rate.FreeDays,
                    route = new {
                        polId = rate.PolId, polName = rate.PolName,
                        poeId = rate.PoeId, poeName = rate.PoeName,
                        podId = rate.PodId, podName = rate.PodName
                    },
                    cargo = new {
                        weightKg = rate.TotalWeightKg,
                        volumeCbm = rate.TotalVolumeCbm,
                        packages = rate.TotalPackages,
                        cargoLinesJson = rate.CargoLinesJson
                    },
                    providers = new {
                        carrierId = rate.CarrierId, carrierName = rate.CarrierName,
                        agentId = rate.AgentId, agentName = rate.AgentName
                    },
                    services = rate.RateServices.Select(service => new {
                        serviceId = service.ServiceId,
                        serviceCode = service.ServiceCode,
                        serviceName = service.ServiceName
                    }).ToArray(),
                    equipment = rate.RateContainers.Select(item => new {
                        equipmentId = item.ContainerTypeId,
                        equipmentCode = item.ContainerTypeCode,
                        equipmentName = item.ContainerTypeName,
                        quantity = item.Quantity,
                        unitWeightsKg = item.UnitWeightsKg
                    }).ToArray(),
                    costLines = rate.RateDetails.Select(item => new {
                        rateDetailId = item.Id,
                        sourceCostId = item.CostId,
                        description = item.Name,
                        costType = item.CostType.ToString(),
                        costDetailType = item.CostDetailType.ToString(),
                        chargeBasis = item.ChargeBasis.ToString(),
                        quantity = item.Quantity,
                        currencyCode = item.CurrencyCode,
                        currencyName = item.CurrencyName,
                        unitCost = item.CostAmount,
                        unitSale = item.SaleAmount,
                        applyDestinationTax = item.ApplyDestinationTax,
                        destinationTaxRate = item.DestinationTaxRate,
                        notes = item.Notes
                    }).ToArray(),
                    documentStorageIds = rate.FinalBackupStorageIds
                };
}
