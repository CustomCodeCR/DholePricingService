using CustomCodeFramework.Core.Domain.Entities;
using Dhole.Pricing.Domain.News.Enums;

namespace Dhole.Pricing.Domain.News.Entities;

public sealed class LogisticsNews : SoftDeletableAggregateRoot<Guid>
{
    private LogisticsNews() { }

    private LogisticsNews(
        Guid id,
        string title,
        string content,
        string? sourceCountry,
        string? sourceOffice,
        DateTime receivedAtUtc,
        Guid? createdBy
    ) : base(id)
    {
        Title = NormalizeRequired(title, nameof(title), 200);
        Content = NormalizeRequired(content, nameof(content), 6000);
        SourceCountry = Normalize(sourceCountry, 120);
        SourceOffice = Normalize(sourceOffice, 160);
        ReceivedAtUtc = receivedAtUtc.Kind == DateTimeKind.Utc
            ? receivedAtUtc
            : receivedAtUtc.ToUniversalTime();
        Status = LogisticsNewsStatus.PendingAnalysis;
        IsActive = true;
        MarkAsCreated(DateTime.UtcNow, createdBy?.ToString());
    }

    public string Title { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public string? SourceCountry { get; private set; }
    public string? SourceOffice { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
    public LogisticsNewsStatus Status { get; private set; }
    public bool IsActive { get; private set; }
    public string? AiSummary { get; private set; }
    public string? AiAnalysisJson { get; private set; }
    public string? EventType { get; private set; }
    public string? Severity { get; private set; }
    public decimal? AiConfidence { get; private set; }
    public int MatchedRateCount { get; private set; }
    public int AppliedRateCount { get; private set; }
    public DateTime? LastProcessedAtUtc { get; private set; }
    public string? ProcessingError { get; private set; }

    public static LogisticsNews Create(
        string? title,
        string content,
        string? sourceCountry,
        string? sourceOffice,
        DateTime? receivedAtUtc,
        Guid? createdBy
    )
    {
        var normalizedTitle = string.IsNullOrWhiteSpace(title)
            ? BuildTitle(content)
            : title.Trim();

        return new LogisticsNews(
            Guid.NewGuid(),
            normalizedTitle,
            content,
            sourceCountry,
            sourceOffice,
            receivedAtUtc ?? DateTime.UtcNow,
            createdBy
        );
    }

    public void MarkProcessed(
        string aiSummary,
        string aiAnalysisJson,
        string? eventType,
        string? severity,
        decimal confidence,
        int matchedRateCount,
        int appliedRateCount,
        Guid? updatedBy
    )
    {
        AiSummary = Normalize(aiSummary, 1200);
        AiAnalysisJson = string.IsNullOrWhiteSpace(aiAnalysisJson) ? null : aiAnalysisJson.Trim();
        EventType = Normalize(eventType, 80);
        Severity = Normalize(severity, 30);
        AiConfidence = Math.Clamp(confidence, 0m, 1m);
        MatchedRateCount = Math.Max(0, matchedRateCount);
        AppliedRateCount = Math.Max(0, appliedRateCount);
        LastProcessedAtUtc = DateTime.UtcNow;
        ProcessingError = null;
        Status = appliedRateCount > 0 ? LogisticsNewsStatus.Applied : LogisticsNewsStatus.NoMatches;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void MarkFailed(string error, Guid? updatedBy)
    {
        ProcessingError = Normalize(error, 2000);
        LastProcessedAtUtc = DateTime.UtcNow;
        Status = LogisticsNewsStatus.Failed;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void SetActive(bool isActive, Guid? updatedBy)
    {
        IsActive = isActive;
        Status = isActive
            ? LogisticsNewsStatus.PendingAnalysis
            : LogisticsNewsStatus.Inactive;
        ProcessingError = null;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    private static string BuildTitle(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "Noticia logística";
        }

        var normalized = content.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return normalized.Length <= 120 ? normalized : $"{normalized[..117]}...";
    }

    private static string NormalizeRequired(string value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{field} es requerido.");
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new InvalidOperationException($"{field} no puede superar {maxLength} caracteres.");
        }

        return normalized;
    }

    private static string? Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
