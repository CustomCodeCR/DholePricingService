namespace Dhole.Pricing.Application.Auditing;

public static class PricingAuditEventTypes
{
    public const string CostCreated = "pricing.cost.created";
    public const string CostUpdated = "pricing.cost.updated";
    public const string CostDeleted = "pricing.cost.deleted";
    public const string CostActivated = "pricing.cost.activated";
    public const string CostInactivated = "pricing.cost.inactivated";

    public const string ImportFclRateCreated = "pricing.import-fcl-rate.created";
    public const string ImportFclRateApproved = "pricing.import-fcl-rate.approved";
    public const string ImportFclRateRejected = "pricing.import-fcl-rate.rejected";
    public const string ImportFclRateCreatedAsRate = "pricing.import-fcl-rate.created-as-rate";
    public const string ImportFclRateDeleted = "pricing.import-fcl-rate.deleted";

    public const string RateHeaderCreated = "pricing.rate-header.created";
    public const string RateHeaderUpdated = "pricing.rate-header.updated";
    public const string RateHeaderDeleted = "pricing.rate-header.deleted";
    public const string RateHeaderAmountsChanged = "pricing.rate-header.amounts-changed";
    public const string RateHeaderApprovalChanged = "pricing.rate-header.approval-changed";

    public const string RateDetailAdded = "pricing.rate-detail.added";
    public const string RateDetailUpdated = "pricing.rate-detail.updated";
    public const string RateDetailRemoved = "pricing.rate-detail.removed";

    public const string FclDecisionCreated = "pricing.fcl-decision.created";
    public const string FclDecisionDeleted = "pricing.fcl-decision.deleted";
}
