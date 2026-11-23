namespace Dhole.Pricing.Persistence.Messaging;

/// <summary>Operations-added service requiring explicit Pricing resolution; no quote price is silently modified.</summary>
public sealed class PricingOperationsServiceReview
{
    public Guid Id { get; set; }
    public Guid QuoteId { get; set; }
    public Guid CaseId { get; set; }
    public string Idtra { get; set; } = "";
    public string ServiceCode { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string Reason { get; set; } = "";
    public Guid RequestedBy { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public string Status { get; set; } = "PENDING";
    public Guid? ResolvedBy { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public string? ResolutionNotes { get; set; }
}
