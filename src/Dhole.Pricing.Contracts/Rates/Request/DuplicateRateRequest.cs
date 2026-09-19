namespace Dhole.Pricing.Contracts.Rates.Request;

public sealed record DuplicateRateRequest(
    DateTime ValidFrom,
    DateTime ValidTo,
    bool ApplyTariff = false,
    string? ClientName = null,
    string? ExecutiveName = null,
    string? IdtraNumber = null
);
