using CustomCodeFramework.Core.Domain.Events;
using Dhole.Pricing.Domain.Costs.Events;
using Dhole.Pricing.Domain.Imports.Events;
using Dhole.Pricing.Domain.Rates.Events;

namespace Dhole.Pricing.Persistence.Messaging;

internal static class DomainEventOutboxMapper
{
    public static string GetEventName(IDomainEvent domainEvent)
    {
        return domainEvent switch
        {
            // Costs
            CostCreatedDomainEvent => "pricing.cost.created",
            CostUpdatedDomainEvent => "pricing.cost.updated",
            CostDeletedDomainEvent => "pricing.cost.deleted",
            CostActivatedDomainEvent => "pricing.cost.activated",
            CostInactivatedDomainEvent => "pricing.cost.inactivated",

            // Import FCL Rates
            //ImportFclRateCreatedDomainEvent => "pricing.import-fcl-rate.created",
            ImportFclRateApprovedDomainEvent => "pricing.import-fcl-rate.approved",
            ImportFclRateRejectDomainEvent => "pricing.import-fcl-rate.rejected",
            ImportFclRateCreatedAsRateDomainEvent => "pricing.import-fcl-rate.created-as-rate",
            ImportFclRateDeletedDomainEvent => "pricing.import-fcl-rate.deleted",

            // Rate Headers
            RateHeaderCreatedDomainEvent => "pricing.rate-header.created",
            RateHeaderUpdatedDomainEvent => "pricing.rate-header.updated",
            RateHeaderDeletedDomainEvent => "pricing.rate-header.deleted",
            RateHeaderAmountsChangedDomainEvent => "pricing.rate-header.amounts-changed",
            //RateHeaderApprovalMarginChangedDomainEvent =>"pricing.rate-header.approval-margin-changed",

            // Rate Details
            //RateDetailAddedDomainEvent => "pricing.rate-detail.added",
            //RateDetailUpdatedDomainEvent => "pricing.rate-detail.updated",
            //RateDetailRemovedDomainEvent => "pricing.rate-detail.removed",

            _ => $"pricing.{domainEvent.GetType().Name}",
        };
    }

    public static string GetEventType(IDomainEvent domainEvent)
    {
        return domainEvent.GetType().FullName ?? domainEvent.GetType().Name;
    }
}
