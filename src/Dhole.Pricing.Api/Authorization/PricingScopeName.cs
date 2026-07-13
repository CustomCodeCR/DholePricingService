namespace Dhole.Pricing.Api.Authorization;

internal static class PricingScopeNames
{
    // Costs
    public const string CostCreate = "pricing.cost.create";
    public const string CostView = "pricing.cost.view";
    public const string CostUpdate = "pricing.cost.update";
    public const string CostDelete = "pricing.cost.delete";
    public const string CostSetActive = "pricing.cost.set-active";
    public const string CostSelect = "pricing.cost.select";

    // Imported FCL rates
    public const string ImportFclRateCreate = "pricing.import-fcl-rate.create";
    public const string ImportFclRateView = "pricing.import-fcl-rate.view";
    public const string ImportFclRateDelete = "pricing.import-fcl-rate.delete";
    public const string ImportFclRateApprove = "pricing.import-fcl-rate.approve";
    public const string ImportFclRateReject = "pricing.import-fcl-rate.reject";
    public const string ImportFclRateCreateAsRate = "pricing.import-fcl-rate.create-as-rate";

    // Rates
    public const string RateCreate = "pricing.rate.create";
    public const string RateView = "pricing.rate.view";
    public const string RateUpdate = "pricing.rate.update";
    public const string RateDelete = "pricing.rate.delete";
    public const string RateSetActive = "pricing.rate.set-active";
    public const string RateSelect = "pricing.rate.select";
    public const string RateApproveLowMargin = "pricing.rate.approve-low-margin";
    public const string RateApproveFreight = "pricing.rate.approve-freight";

    // FCL rate details
    public const string FclRateDetailCreate = "pricing.fcl-rate-detail.create";
    public const string FclRateDetailUpdate = "pricing.fcl-rate-detail.update";
    public const string FclRateDetailDelete = "pricing.fcl-rate-detail.delete";

    // Rate cost details
    public const string RateCostDetailCreate = "pricing.rate-cost-detail.create";
    public const string RateCostDetailUpdate = "pricing.rate-cost-detail.update";
    public const string RateCostDetailDelete = "pricing.rate-cost-detail.delete";

    // FCL decisions
    public const string FclDecisionCreate = "pricing.fcl-decisions.create";
    public const string FclDecisionView = "pricing.fcl-decisions.view";
    public const string FclDecisionDelete = "pricing.fcl-decisions.delete";
}
