namespace Dhole.Pricing.Domain.Competitors.Entities;

/// <summary>Storage reference to a standalone interactive HTML competitor report.</summary>
public sealed class CompetitorAnalysisDocument
{
    public Guid Id { get; set; }
    public Guid StorageId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public long SizeInBytes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
