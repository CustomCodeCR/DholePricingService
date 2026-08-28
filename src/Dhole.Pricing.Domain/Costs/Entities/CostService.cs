namespace Dhole.Pricing.Domain.Costs.Entities;

public sealed class CostService
{
    private CostService() { }

    internal CostService(Guid costId, Guid serviceId, string serviceName, string serviceCode)
    {
        if (costId == Guid.Empty || serviceId == Guid.Empty)
            throw new InvalidOperationException("El costo y el servicio de Pricing son obligatorios.");
        if (string.IsNullOrWhiteSpace(serviceName) || string.IsNullOrWhiteSpace(serviceCode))
            throw new InvalidOperationException("El servicio de Pricing debe incluir nombre y código.");

        CostId = costId;
        ServiceId = serviceId;
        ServiceName = serviceName.Trim();
        ServiceCode = serviceCode.Trim();
    }

    internal void UpdateSnapshot(string serviceName, string serviceCode)
    {
        if (string.IsNullOrWhiteSpace(serviceName) || string.IsNullOrWhiteSpace(serviceCode))
            throw new InvalidOperationException("El servicio de Pricing debe incluir nombre y código.");
        ServiceName = serviceName.Trim();
        ServiceCode = serviceCode.Trim();
    }

    public Guid CostId { get; private set; }
    public Guid ServiceId { get; private set; }
    public string ServiceName { get; private set; } = string.Empty;
    public string ServiceCode { get; private set; } = string.Empty;
}

public sealed record CostServiceSelection(Guid Id, string Name, string Code);
