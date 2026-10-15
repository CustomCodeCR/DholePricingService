namespace Dhole.Pricing.Domain.Imports.Entities;

/// <summary>A durable work item for an uploaded manual rate file. Never return FileContent to the API consumer.</summary>
public sealed class ManualImportExtractionJob
{
    public Guid Id { get; set; }
    public Guid RequestedBy { get; set; }
    public string? RequestedByName { get; set; }
    public string? ClientRequestKey { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public string? ProfileSlug { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public byte[] FileContent { get; set; } = [];
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public string? ResultJson { get; set; }
    public string? ErrorMessage { get; set; }
}
