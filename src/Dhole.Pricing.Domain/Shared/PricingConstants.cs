namespace Dhole.Pricing.Domain.Shared;

public static class PricingConstants
{
    public const string ServiceName = "Pricing";

    public static class Scopes
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

    public static class Audit
    {
        public static class EntityTypes
        {
            public const string Cost = "Cost";
            public const string ImportFclRate = "ImportFclRate";
            public const string RateHeader = "RateHeader";
            public const string FclRateDetail = "FclRateDetail";
            public const string RateCostDetail = "RateCostDetail";
            public const string FclDecision = "FclDecision";
        }

        public static class Actions
        {
            public const string Created = "created";
            public const string Updated = "updated";
            public const string Deleted = "deleted";
            public const string Inactivated = "inactivated";
            public const string Activated = "activated";
            public const string Approved = "approved";
            public const string Rejected = "rejected";
            public const string ImportedOnly = "imported-only";
            public const string CreatedAsRate = "created-as-rate";
            public const string AmountsChanged = "amounts-changed";
            public const string Added = "added";
            public const string Removed = "removed";
        }

        public static class EventTypes
        {
            // Costs
            public const string CostCreated = "pricing.cost.created";
            public const string CostUpdated = "pricing.cost.updated";
            public const string CostDeleted = "pricing.cost.deleted";
            public const string CostActivated = "pricing.cost.activated";
            public const string CostInactivated = "pricing.cost.inactivated";

            // Imported FCL rates
            public const string ImportFclRateCreated = "pricing.import-fcl-rate.created";
            public const string ImportFclRateApproved = "pricing.import-fcl-rate.approved";
            public const string ImportFclRateRejected = "pricing.import-fcl-rate.rejected";
            public const string ImportFclRateImportedOnly = "pricing.import-fcl-rate.imported-only";
            public const string ImportFclRateCreatedAsRate =
                "pricing.import-fcl-rate.created-as-rate";
            public const string ImportFclRateDeleted = "pricing.import-fcl-rate.deleted";

            // Rate headers
            public const string RateHeaderCreated = "pricing.rate-header.created";
            public const string RateHeaderUpdated = "pricing.rate-header.updated";
            public const string RateHeaderDeleted = "pricing.rate-header.deleted";
            public const string RateHeaderActivated = "pricing.rate-header.activated";
            public const string RateHeaderInactivated = "pricing.rate-header.inactivated";
            public const string RateHeaderAmountsChanged = "pricing.rate-header.amounts-changed";

            // FCL rate details
            public const string FclRateDetailAdded = "pricing.fcl-rate-detail.added";
            public const string FclRateDetailUpdated = "pricing.fcl-rate-detail.updated";
            public const string FclRateDetailRemoved = "pricing.fcl-rate-detail.removed";

            // Rate cost details
            public const string RateCostDetailAdded = "pricing.rate-cost-detail.added";
            public const string RateCostDetailUpdated = "pricing.rate-cost-detail.updated";
            public const string RateCostDetailRemoved = "pricing.rate-cost-detail.removed";

            // FCL decisions
            public const string FclDecisionCreated = "pricing.fcl-decision.created";
            public const string FclDecisionDeleted = "pricing.fcl-decision.deleted";
        }
    }
}
