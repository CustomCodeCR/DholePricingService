namespace Dhole.Pricing.Contracts.Costs.Response;

/// <summary>
/// Ordered active catalog and costs eligible for one quotation context, from the
/// same database snapshot. Each line retains its CostId and full relationships.
/// </summary>
public sealed record CostWizardBundleDto(
    IReadOnlyCollection<CostSelectDto> Catalog,
    IReadOnlyCollection<CostSelectDto> Applicable
);
