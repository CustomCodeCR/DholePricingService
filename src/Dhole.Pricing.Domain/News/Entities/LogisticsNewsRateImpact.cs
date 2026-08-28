using CustomCodeFramework.Core.Domain.Entities;

namespace Dhole.Pricing.Domain.News.Entities;

public sealed class LogisticsNewsRateImpact : Entity<Guid>
{
    private LogisticsNewsRateImpact() { }

    private LogisticsNewsRateImpact(
        Guid id,
        Guid logisticsNewsId,
        Guid importFclRateId,
        string matchReason,
        decimal confidence,
        string appliedComment,
        DateTime appliedAtUtc
    ) : base(id)
    {
        LogisticsNewsId = logisticsNewsId;
        ImportFclRateId = importFclRateId;
        MatchReason = matchReason.Trim();
        Confidence = Math.Clamp(confidence, 0m, 1m);
        AppliedComment = appliedComment.Trim();
        AppliedAtUtc = appliedAtUtc;
    }

    public Guid LogisticsNewsId { get; private set; }
    public Guid ImportFclRateId { get; private set; }
    public string MatchReason { get; private set; } = string.Empty;
    public decimal Confidence { get; private set; }
    public string AppliedComment { get; private set; } = string.Empty;
    public DateTime AppliedAtUtc { get; private set; }

    public static LogisticsNewsRateImpact Create(
        Guid logisticsNewsId,
        Guid importFclRateId,
        string matchReason,
        decimal confidence,
        string appliedComment
    )
    {
        if (logisticsNewsId == Guid.Empty || importFclRateId == Guid.Empty)
        {
            throw new InvalidOperationException("La noticia y la tarifa afectada son requeridas.");
        }

        if (string.IsNullOrWhiteSpace(matchReason) || string.IsNullOrWhiteSpace(appliedComment))
        {
            throw new InvalidOperationException("La razón de coincidencia y el comentario aplicado son requeridos.");
        }

        return new LogisticsNewsRateImpact(
            Guid.NewGuid(),
            logisticsNewsId,
            importFclRateId,
            matchReason.Length <= 1000 ? matchReason : matchReason[..1000],
            confidence,
            appliedComment.Length <= 2000 ? appliedComment : appliedComment[..2000],
            DateTime.UtcNow
        );
    }
}
