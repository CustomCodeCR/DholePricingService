namespace Dhole.Pricing.Contracts.Rates.Request;

public sealed record DuplicateRateRequest(
    DateTime ValidFrom,
    DateTime ValidTo,
    bool ApplyTariff = false,
    string? ClientName = null,
    string? ExecutiveName = null,
    string? IdtraNumber = null,
    bool IdenticalCopy = false
)
{
    // Optional for existing REST consumers; new tariff applications resolve these in CRM.
    public Guid? ClientAccountId { get; init; }
    public Guid? ClientLeadId { get; init; }
}

