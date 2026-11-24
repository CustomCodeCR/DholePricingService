using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Api.Endpoints;

public static class SellerRateDecisionPolicy
{
    public static bool RequiresPublishedRevision(RateStatus current, RateStatus target) =>
        current == RateStatus.Open && target is (RateStatus.AcceptedByClient or RateStatus.RejectedByClient);

    public static bool CanDecideWithoutPublishedRevision(RateStatus current, RateStatus target) =>
        target switch
        {
            RateStatus.Cancelled => current == RateStatus.AcceptedByClient,
            RateStatus.AcceptedByClient or RateStatus.RejectedByClient =>
                current is RateStatus.Sent or RateStatus.RequestedByClient or RateStatus.Expired,
            _ => false
        };
}
