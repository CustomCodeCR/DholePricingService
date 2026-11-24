namespace Dhole.Pricing.Domain.Rates.Entities;

/// <summary>
/// Operations-originated commercial review. It is not a quotation revision and
/// never implicitly modifies commercial tariffs, sales or margins.
/// </summary>
public sealed class PricingOperationsReviewRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperationsRequestId { get; set; }
    public Guid CaseId { get; set; }
    public Guid CompanyId { get; set; }
    public string Idtra { get; set; } = "";
    public Guid ServiceId { get; set; }
    public Guid? QuoteId { get; set; }
    public Guid? AcceptedOptionId { get; set; }
    public string ServiceCode { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "PENDING_REVIEW";
    public Guid RequestedBy { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
}
