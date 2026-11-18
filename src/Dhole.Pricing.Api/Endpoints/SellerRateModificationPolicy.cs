using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Api.Endpoints;

/// <summary>
/// Sellers may propose changes only while a quotation has never been accepted.
/// Pricing users continue to edit accepted quotations via pricing.rate.update.
/// </summary>
public static class SellerRateModificationPolicy
{
    public static bool RequiresPricing(RateStatus status, bool previouslyAccepted)
        => previouslyAccepted || status == RateStatus.AcceptedByClient;

    public static bool CanSellerRequest(
        RateStatus status, bool sellerReceived, bool previouslyAccepted, bool isDeleted)
        => !isDeleted
            && !RequiresPricing(status, previouslyAccepted)
            && status is not (RateStatus.Closed or RateStatus.Cancelled)
            && (sellerReceived || status is RateStatus.Sent or RateStatus.RequestedByClient
                or RateStatus.RejectedByClient or RateStatus.Expired);
}
