namespace Dhole.Pricing.Domain.Rates.Enums;

public enum RateStatus
{
    PendingApproval = 0,
    Approved = 1,
    Rejected = 2,
    Draft = 3,
    Sent = 4,
    AcceptedByClient = 5,
    RejectedByClient = 6,
}
