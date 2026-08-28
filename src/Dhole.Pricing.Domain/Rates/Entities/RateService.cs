namespace Dhole.Pricing.Domain.Rates.Entities;

public sealed class RateService
{
    private RateService() { }

    internal RateService(Guid rateHeaderId, Guid serviceId, string serviceName, string serviceCode)
    {
        if (rateHeaderId == Guid.Empty || serviceId == Guid.Empty)
            throw new InvalidOperationException("La tarifa y el servicio son obligatorios.");
        if (string.IsNullOrWhiteSpace(serviceName) || string.IsNullOrWhiteSpace(serviceCode))
            throw new InvalidOperationException("El servicio debe incluir nombre y código.");
        RateHeaderId = rateHeaderId;
        ServiceId = serviceId;
        ServiceName = serviceName.Trim();
        ServiceCode = serviceCode.Trim();
    }

    public Guid RateHeaderId { get; private set; }
    public Guid ServiceId { get; private set; }
    public string ServiceName { get; private set; } = string.Empty;
    public string ServiceCode { get; private set; } = string.Empty;
}

public sealed record RateServiceSelection(Guid Id, string Name, string Code);
