namespace Dhole.Pricing.Domain.Rates.Enums;

public enum RateStatus
{
    PendingApproval = 0,
    ApprovedByManagement = 1,
    RejectedByManagement = 2,
    Open = 3,
    Sent = 4,
    AcceptedByClient = 5,
    RejectedByClient = 6,
    RequestedByClient = 7,
    Closed = 8,
    Expired = 9,
}
