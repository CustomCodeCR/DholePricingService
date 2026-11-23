namespace Dhole.Pricing.Persistence.Messaging;

/// <summary>Pricing owns commercial decisions on services requested by Operations.</summary>
public sealed class PricingOperationsServiceReview
{
    public Guid Id { get; set; }
    public Guid CaseId { get; set; }
    public Guid? QuoteId { get; set; }
    public Guid? QuoteGroupId { get; set; }
    public Guid ServiceCatalogId { get; set; }
    public string ServiceCode { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string Reason { get; set; } = "";
    public Guid RequestedBy { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public string Status { get; set; } = "PENDING_PRICING";
    public string? ResolutionReason { get; set; }
    public Guid? ResolvedBy { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
}
