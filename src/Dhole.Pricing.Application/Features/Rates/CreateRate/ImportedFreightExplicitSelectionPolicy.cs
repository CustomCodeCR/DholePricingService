using Dhole.Pricing.Domain.Costs.Enums;

namespace Dhole.Pricing.Application.Features.Rates.CreateRate;

/// <summary>SourceImportFclRateId is price context, not permission to add Freight.</summary>
public static class ImportedFreightExplicitSelectionPolicy
{
    public static bool ShouldInclude(Guid? sourceImportFclRateId,
        CreateRateDetailCommandItem? requestedFreight) =>
        sourceImportFclRateId.HasValue
        && requestedFreight?.CostDetailType == CostDetailType.Freight;
}
