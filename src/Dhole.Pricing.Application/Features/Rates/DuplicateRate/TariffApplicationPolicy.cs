using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.Features.Rates.DuplicateRate;

/// <summary>
/// Commercial eligibility for creating a client quote from a reusable master.
/// A future start date must not prevent preparing the quote before sailing.
/// The derived quote keeps both original validity dates.
/// </summary>
public static class TariffApplicationPolicy
{
    public static bool CanApply(
        RateType rateType,
        string? masterClientName,
        RateStatus status,
        DateTime validFrom,
        DateTime validTo,
        DateTime referenceUtcDate
    )
    {
        return rateType == RateType.Tariff
            && masterClientName?.Contains("TARIFARIO", StringComparison.OrdinalIgnoreCase) == true
            && status is (
                RateStatus.ApprovedByManagement
                or RateStatus.Open
                or RateStatus.Sent
                or RateStatus.RequestedByClient
                or RateStatus.AcceptedByClient
            )
            && validFrom.Date <= validTo.Date
            && validTo.Date >= referenceUtcDate.Date;
    }
}
