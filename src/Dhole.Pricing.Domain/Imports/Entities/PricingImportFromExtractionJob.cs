using CustomCodeFramework.Core.Domain.Entities;
using Dhole.Pricing.Domain.Imports.Enums;

namespace Dhole.Pricing.Domain.Imports.Entities;

public sealed class PricingImportFromExtractionJob : AuditableAggregateRoot<Guid>
{
    private PricingImportFromExtractionJob() { }

    private PricingImportFromExtractionJob(
        Guid id,
        Guid externalRequestId,
        Guid emailExtractionJobId,
        Guid extractionExecutionId,
        Guid pricingImportId,
        string payloadJson,
        string correlationId,
        int maxAttemptCount
    )
        : base(id)
    {
        ExternalRequestId = externalRequestId;
        EmailExtractionJobId = emailExtractionJobId;
        ExtractionExecutionId = extractionExecutionId;
        PricingImportId = pricingImportId;
        PayloadJson = Required(payloadJson, "El payload de importación es requerido.");
        CorrelationId = Required(correlationId, "El CorrelationId es requerido.");
        MaxAttemptCount = Math.Max(1, maxAttemptCount);
        Status = PricingImportFromExtractionJobStatus.Pending;

        MarkAsCreated(DateTime.UtcNow, null);
    }

    public Guid ExternalRequestId { get; private set; }

    public Guid EmailExtractionJobId { get; private set; }

    public Guid ExtractionExecutionId { get; private set; }

    public Guid PricingImportId { get; private set; }

    public string PayloadJson { get; private set; } = string.Empty;

    public string CorrelationId { get; private set; } = string.Empty;

    public PricingImportFromExtractionJobStatus Status { get; private set; }

    public int AttemptCount { get; private set; }

    public int MaxAttemptCount { get; private set; }

    public DateTime? NextAttemptAtUtc { get; private set; }

    public string? LeaseOwner { get; private set; }

    public DateTime? LeaseExpiresAtUtc { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public int PersistedRows { get; private set; }

    public int SkippedRows { get; private set; }

    public DateTime? StartedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public int Version { get; private set; } = 1;

    public static PricingImportFromExtractionJob Create(
        Guid externalRequestId,
        Guid emailExtractionJobId,
        Guid extractionExecutionId,
        Guid pricingImportId,
        string payloadJson,
        string correlationId,
        int maxAttemptCount
    )
    {
        if (
            externalRequestId == Guid.Empty
            || emailExtractionJobId == Guid.Empty
            || extractionExecutionId == Guid.Empty
            || pricingImportId == Guid.Empty
        )
        {
            throw new InvalidOperationException(
                "La solicitud, el trabajo, la extracción y el lote son requeridos."
            );
        }

        return new PricingImportFromExtractionJob(
            Guid.NewGuid(),
            externalRequestId,
            emailExtractionJobId,
            extractionExecutionId,
            pricingImportId,
            payloadJson,
            correlationId,
            maxAttemptCount
        );
    }

    public void MarkProcessing(string leaseOwner, DateTime leaseExpiresAtUtc)
    {
        if (
            Status
            is not PricingImportFromExtractionJobStatus.Pending
                and not PricingImportFromExtractionJobStatus.RetryScheduled
        )
        {
            throw new InvalidOperationException(
                "Solo un trabajo pendiente o reprogramado puede reclamarse."
            );
        }

        if (string.IsNullOrWhiteSpace(leaseOwner))
        {
            throw new InvalidOperationException("El propietario del lease es requerido.");
        }

        var now = DateTime.UtcNow;
        Status = PricingImportFromExtractionJobStatus.Processing;
        AttemptCount++;
        NextAttemptAtUtc = null;
        LeaseOwner = leaseOwner.Trim();
        LeaseExpiresAtUtc = leaseExpiresAtUtc > now
            ? leaseExpiresAtUtc
            : now.AddMinutes(5);
        StartedAtUtc ??= now;
        ErrorCode = null;
        ErrorMessage = null;
        Touch(now);
    }

    public void MarkCompleted(int persistedRows, int skippedRows)
    {
        if (Status == PricingImportFromExtractionJobStatus.Completed)
        {
            return;
        }

        if (Status != PricingImportFromExtractionJobStatus.Processing)
        {
            throw new InvalidOperationException(
                "Solo un trabajo en procesamiento puede completarse."
            );
        }

        PersistedRows = Math.Max(0, persistedRows);
        SkippedRows = Math.Max(0, skippedRows);
        Status = PricingImportFromExtractionJobStatus.Completed;
        ErrorCode = null;
        ErrorMessage = null;
        CompletedAtUtc = DateTime.UtcNow;
        ReleaseLeaseCore();
        Touch(CompletedAtUtc.Value);
    }

    public void ScheduleRetry(
        string errorCode,
        string errorMessage,
        DateTime nextAttemptAtUtc
    )
    {
        if (Status != PricingImportFromExtractionJobStatus.Processing)
        {
            throw new InvalidOperationException(
                "Solo un trabajo en procesamiento puede reprogramarse."
            );
        }

        ErrorCode = Required(errorCode, "El código de error es requerido.");
        ErrorMessage = Limit(
            Required(errorMessage, "El mensaje de error es requerido."),
            4000
        );
        Status = PricingImportFromExtractionJobStatus.RetryScheduled;
        NextAttemptAtUtc = nextAttemptAtUtc > DateTime.UtcNow
            ? nextAttemptAtUtc
            : DateTime.UtcNow;
        ReleaseLeaseCore();
        Touch(DateTime.UtcNow);
    }

    public void MarkFailed(string errorCode, string errorMessage)
    {
        if (Status == PricingImportFromExtractionJobStatus.Completed)
        {
            throw new InvalidOperationException(
                "Un trabajo completado no puede marcarse como fallido."
            );
        }

        ErrorCode = Required(errorCode, "El código de error es requerido.");
        ErrorMessage = Limit(
            Required(errorMessage, "El mensaje de error es requerido."),
            4000
        );
        Status = PricingImportFromExtractionJobStatus.Failed;
        CompletedAtUtc = DateTime.UtcNow;
        NextAttemptAtUtc = null;
        ReleaseLeaseCore();
        Touch(CompletedAtUtc.Value);
    }

    private void ReleaseLeaseCore()
    {
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
    }

    private void Touch(DateTime now)
    {
        Version++;
        MarkAsUpdated(now, null);
    }

    private static string Required(string? value, string errorMessage)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(errorMessage)
            : value.Trim();
    }

    private static string Limit(string value, int maximumLength)
    {
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }
}
