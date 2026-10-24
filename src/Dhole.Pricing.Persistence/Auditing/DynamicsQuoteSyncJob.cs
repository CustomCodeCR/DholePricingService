namespace Dhole.Pricing.Persistence.Auditing;

/// <summary>
/// Immutable acceptance snapshot and durable delivery state. The API key is never persisted.
/// One CRM Quote per accepted rate revision, even after an HTTP or worker retry.
/// </summary>
public sealed class DynamicsQuoteSyncJob
{
    public Guid Id { get; set; }
    public Guid RateId { get; set; }
    public int RevisionNumber { get; set; }
    public Guid EventId { get; set; }
    public string PayloadJson { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public int Attempts { get; set; }
    // 0 = initial attempt; 1..3 = separately approved recoveries after confirmed 4xx.
    public int RecoveryAttempt { get; set; }
    public string? RecoveryReason { get; set; }
    public string? RecoveryApprovedBy { get; set; }
    public DateTime? RecoveryApprovedAtUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public string? LastError { get; set; }
    public string? ResponseJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}
