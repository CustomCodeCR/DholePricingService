namespace Dhole.Pricing.Domain.Competitors.Entities;

/// <summary>
/// Durable copy of the source PDF while an Average competitor import is unfinished.
/// A Processing record without a payload predates this feature and needs re-upload.
/// </summary>
public sealed class CompetitorTariffImportPayload
{
    public Guid CompetitorTariffId { get; set; }
    public byte[] FileContent { get; set; } = [];
    public string FileHash { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public DateTime? FallbackFrom { get; set; }
    public DateTime? FallbackTo { get; set; }
    public Guid? RequestedBy { get; set; }
    public string? RequestedByName { get; set; }
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LeaseUntilUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
