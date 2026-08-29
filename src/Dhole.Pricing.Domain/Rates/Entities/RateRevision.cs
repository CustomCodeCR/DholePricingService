namespace Dhole.Pricing.Domain.Rates.Entities;

public sealed class RateRevision
{
    private RateRevision() { }

    private RateRevision(Guid id, Guid rateHeaderId, int revisionNumber, string status, string rateName,
        string? idtraNumber, string? quoNumber, decimal totalSaleUsd, decimal totalSaleCrc,
        decimal marginPercentage, string snapshotJson, Guid? createdBy)
    {
        Id = id;
        RateHeaderId = rateHeaderId;
        RevisionNumber = revisionNumber;
        Status = status.Trim();
        RateName = rateName.Trim();
        IdtraNumber = string.IsNullOrWhiteSpace(idtraNumber) ? null : idtraNumber.Trim();
        QuoNumber = string.IsNullOrWhiteSpace(quoNumber) ? null : quoNumber.Trim();
        TotalSaleUsd = totalSaleUsd;
        TotalSaleCrc = totalSaleCrc;
        MarginPercentage = marginPercentage;
        SnapshotJson = snapshotJson;
        CreatedAtUtc = DateTime.UtcNow;
        CreatedBy = createdBy;
    }

    public Guid Id { get; private set; }
    public Guid RateHeaderId { get; private set; }
    public int RevisionNumber { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string RateName { get; private set; } = string.Empty;
    public string? IdtraNumber { get; private set; }
    public string? QuoNumber { get; private set; }
    public decimal TotalSaleUsd { get; private set; }
    public decimal TotalSaleCrc { get; private set; }
    public decimal MarginPercentage { get; private set; }
    public string SnapshotJson { get; private set; } = "{}";
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }

    public static RateRevision Create(Guid rateHeaderId, int revisionNumber, string status, string rateName,
        string? idtraNumber, string? quoNumber, decimal totalSaleUsd, decimal totalSaleCrc,
        decimal marginPercentage, string snapshotJson, Guid? createdBy)
    {
        if (rateHeaderId == Guid.Empty || revisionNumber < 1) throw new InvalidOperationException("La revisión de tarifa no es válida.");
        if (string.IsNullOrWhiteSpace(status) || string.IsNullOrWhiteSpace(rateName) || string.IsNullOrWhiteSpace(snapshotJson))
            throw new InvalidOperationException("La revisión requiere estado, nombre e instantánea.");
        return new RateRevision(Guid.NewGuid(), rateHeaderId, revisionNumber, status, rateName, idtraNumber,
            quoNumber, totalSaleUsd, totalSaleCrc, marginPercentage, snapshotJson, createdBy);
    }
}
