using CustomCodeFramework.Core.Domain.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Domain.Rates.Entities;

public sealed class RateComparison : Entity<Guid>
{
    private readonly List<RateComparisonDetail> _details = [];

    private RateComparison() { }

    private RateComparison(
        Guid id,
        Guid sourceImportFclRateId,
        Guid comparedRateHeaderId,
        string comparedRateCode,
        RateComparisonType comparisonType,
        Guid polId,
        string polName,
        Guid poeId,
        string poeName,
        Guid containerTypeId,
        string containerTypeName,
        string currencyCode,
        decimal baselineCostAmount,
        decimal baselineSaleAmount,
        decimal candidateCostAmount,
        decimal candidateSaleAmount,
        decimal baselineComparedAmount,
        decimal candidateComparedAmount,
        string candidatePayloadJson
    ) : base(id)
    {
        SourceImportFclRateId = sourceImportFclRateId;
        ComparedRateHeaderId = comparedRateHeaderId;
        ComparedRateCode = comparedRateCode.Trim();
        ComparisonType = comparisonType;
        PolId = polId;
        PolName = polName.Trim();
        PoeId = poeId;
        PoeName = poeName.Trim();
        ContainerTypeId = containerTypeId;
        ContainerTypeName = containerTypeName.Trim();
        CurrencyCode = currencyCode.Trim();
        BaselineCostAmount = baselineCostAmount;
        BaselineSaleAmount = baselineSaleAmount;
        CandidateCostAmount = candidateCostAmount;
        CandidateSaleAmount = candidateSaleAmount;
        BaselineComparedAmount = baselineComparedAmount;
        CandidateComparedAmount = candidateComparedAmount;
        SavingsAmount = Math.Max(0m, baselineComparedAmount - candidateComparedAmount);
        SavingsPercent = baselineComparedAmount > 0m
            ? Math.Round(SavingsAmount / baselineComparedAmount * 100m, 2)
            : 0m;
        CandidatePayloadJson = candidatePayloadJson;
        Status = RateComparisonStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid SourceImportFclRateId { get; private set; }
    public Guid ComparedRateHeaderId { get; private set; }
    public string ComparedRateCode { get; private set; } = string.Empty;
    public RateComparisonType ComparisonType { get; private set; }
    public RateComparisonStatus Status { get; private set; }
    public Guid PolId { get; private set; }
    public string PolName { get; private set; } = string.Empty;
    public Guid PoeId { get; private set; }
    public string PoeName { get; private set; } = string.Empty;
    public Guid ContainerTypeId { get; private set; }
    public string ContainerTypeName { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal BaselineCostAmount { get; private set; }
    public decimal BaselineSaleAmount { get; private set; }
    public decimal CandidateCostAmount { get; private set; }
    public decimal CandidateSaleAmount { get; private set; }
    public decimal BaselineComparedAmount { get; private set; }
    public decimal CandidateComparedAmount { get; private set; }
    public decimal SavingsAmount { get; private set; }
    public decimal SavingsPercent { get; private set; }
    public string CandidatePayloadJson { get; private set; } = "{}";
    public Guid? CreatedRateHeaderId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public Guid? ResolvedBy { get; private set; }
    public IReadOnlyCollection<RateComparisonDetail> Details => _details.AsReadOnly();

    public static RateComparison Create(
        Guid sourceImportFclRateId,
        Guid comparedRateHeaderId,
        string comparedRateCode,
        RateComparisonType comparisonType,
        Guid polId,
        string polName,
        Guid poeId,
        string poeName,
        Guid containerTypeId,
        string containerTypeName,
        string currencyCode,
        decimal baselineCostAmount,
        decimal baselineSaleAmount,
        decimal candidateCostAmount,
        decimal candidateSaleAmount,
        decimal baselineComparedAmount,
        decimal candidateComparedAmount,
        string candidatePayloadJson)
        => new(
            Guid.NewGuid(), sourceImportFclRateId, comparedRateHeaderId, comparedRateCode,
            comparisonType, polId, polName, poeId, poeName, containerTypeId,
            containerTypeName, currencyCode, baselineCostAmount, baselineSaleAmount,
            candidateCostAmount, candidateSaleAmount, baselineComparedAmount,
            candidateComparedAmount, candidatePayloadJson);

    public void AddDetail(RateComparisonDetail detail)
    {
        if (detail.RateComparisonId != Id)
            throw new InvalidOperationException("El detalle no corresponde a la comparación.");
        _details.Add(detail);
    }

    public void MarkCreated(Guid rateHeaderId, Guid? userId)
    {
        if (Status != RateComparisonStatus.Pending) return;
        CreatedRateHeaderId = rateHeaderId;
        Status = RateComparisonStatus.Created;
        ResolvedAtUtc = DateTime.UtcNow;
        ResolvedBy = userId;
    }

    public void Dismiss(Guid? userId)
    {
        if (Status != RateComparisonStatus.Pending) return;
        Status = RateComparisonStatus.Dismissed;
        ResolvedAtUtc = DateTime.UtcNow;
        ResolvedBy = userId;
    }
}
