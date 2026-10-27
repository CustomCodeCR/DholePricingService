namespace Dhole.Pricing.Contracts.Rates.Request;

// La revisión observada por el usuario debe seguir siendo la vigente al reenviar.
public sealed record ResendRateRevisionRequest(int RevisionNumber);
