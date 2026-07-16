namespace Dhole.Pricing.Domain.Rates.Enums;

public enum RateStatus
{
    PendingApproval = 0,
    Approved = 1,
    Rejected = 2,
    Draft = 3,
    Send = 4,
    AcceptedForClient = 5,
    RejectedForClient = 6,
}
