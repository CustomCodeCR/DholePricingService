using Dhole.Pricing.Domain.Costs.Enums;

namespace Dhole.Pricing.Application.Features.Rates.CreateRate;

/// <summary>
/// An imported FCL rate can be used as a price source without adding ocean freight
/// to the customer's new quote. Presence of SourceImportFclRateId is NOT consent
/// to bill freight; the request must include a selected Freight RateDetail.
/// </summary>
public static class ImportedFreightExplicitSelectionPolicy
{
    public static bool ShouldInclude(Guid? sourceImportFclRateId,
        CreateRateDetailCommandItem? requestedFreight) =>
        sourceImportFclRateId.HasValue
        && requestedFreight?.CostDetailType == CostDetailType.Freight;
}
