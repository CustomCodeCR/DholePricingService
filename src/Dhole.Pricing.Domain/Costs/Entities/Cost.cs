using CustomCodeFramework.Core.Domain.Entities;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Costs.Events;

namespace Dhole.Pricing.Domain.Costs.Entities;

public sealed class Cost : SoftDeletableAggregateRoot<Guid>
{
    private Cost() { }

    private Cost(
        Guid id,
        string name,
        CostType costType,
        CostDetailType costDetailType,
        Guid? carrierId,
        string? carrierName,
        string? carrierCode,
        Guid? agentId,
        string? agentName,
        string? agentCode,
        Guid? portId,
        string? portName,
        string? portCode,
        CostPortRole? portRole,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes,
        bool isAccountant,
        Guid? createdBy
    ) : base(id)
    {
        Apply(
            name,
            costType,
            costDetailType,
            carrierId,
            carrierName,
            carrierCode,
            agentId,
            agentName,
            agentCode,
            portId,
            portName,
            portCode,
            portRole,
            currencyId,
            currencyName,
            currencyCode,
            costAmount,
            saleAmount,
            notes,
            isAccountant
        );

        IsActive = true;
        MarkAsCreated(DateTime.UtcNow, createdBy?.ToString());
    }

    public string Name { get; private set; } = string.Empty;
    public CostType CostType { get; private set; }
    public CostDetailType CostDetailType { get; private set; }
    public Guid? CarrierId { get; private set; }
    public string? CarrierName { get; private set; }
    public string? CarrierCode { get; private set; }
    public Guid? AgentId { get; private set; }
    public string? AgentName { get; private set; }
    public string? AgentCode { get; private set; }
    public Guid? PortId { get; private set; }
    public string? PortName { get; private set; }
    public string? PortCode { get; private set; }
    public CostPortRole? PortRole { get; private set; }
    public Guid CurrencyId { get; private set; }
    public string CurrencyName { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal CostAmount { get; private set; }
    public decimal SaleAmount { get; private set; }
    public decimal UtilityAmount { get; private set; }
    public string? Notes { get; private set; }
    private bool _isAccountant;
    public bool IsAccountant
    {
        get => _isAccountant || IsFreightPerContainer(CostDetailType);
        private set => _isAccountant = value;
    }
    public bool IsActive { get; private set; }

    public static Cost Create(
        string name,
        CostType costType,
        CostDetailType costDetailType,
        Guid? carrierId,
        string? carrierName,
        string? carrierCode,
        Guid? agentId,
        string? agentName,
        string? agentCode,
        Guid? portId,
        string? portName,
        string? portCode,
        CostPortRole? portRole,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes,
        bool isAccountant,
        Guid? createdBy
    )
    {
        var cost = new Cost(
            Guid.NewGuid(), name, costType, costDetailType,
            carrierId, carrierName, carrierCode,
            agentId, agentName, agentCode,
            portId, portName, portCode, portRole,
            currencyId, currencyName, currencyCode,
            costAmount, saleAmount, notes, isAccountant, createdBy
        );

        cost.AddDomainEvent(new CostCreatedDomainEvent(cost.Id, cost.Name, createdBy));
        return cost;
    }

    public void Update(
        string name,
        CostType costType,
        CostDetailType costDetailType,
        Guid? carrierId,
        string? carrierName,
        string? carrierCode,
        Guid? agentId,
        string? agentName,
        string? agentCode,
        Guid? portId,
        string? portName,
        string? portCode,
        CostPortRole? portRole,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes,
        bool isAccountant,
        Guid? updatedBy
    )
    {
        Apply(
            name, costType, costDetailType,
            carrierId, carrierName, carrierCode,
            agentId, agentName, agentCode,
            portId, portName, portCode, portRole,
            currencyId, currencyName, currencyCode,
            costAmount, saleAmount, notes, isAccountant
        );

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
        AddDomainEvent(new CostUpdatedDomainEvent(Id, Name, updatedBy));
    }

    private void Apply(
        string name,
        CostType costType,
        CostDetailType costDetailType,
        Guid? carrierId,
        string? carrierName,
        string? carrierCode,
        Guid? agentId,
        string? agentName,
        string? agentCode,
        Guid? portId,
        string? portName,
        string? portCode,
        CostPortRole? portRole,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes,
        bool isAccountant
    )
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("El nombre del costo es obligatorio.");
        if (currencyId == Guid.Empty || string.IsNullOrWhiteSpace(currencyName) || string.IsNullOrWhiteSpace(currencyCode))
            throw new InvalidOperationException("La moneda del costo es obligatoria.");
        if (costAmount < 0m || saleAmount < 0m)
            throw new InvalidOperationException("Los montos del costo no pueden ser negativos.");

        var normalizedCarrierId = NormalizeId(carrierId);
        var normalizedAgentId = NormalizeId(agentId);
        var normalizedPortId = NormalizeId(portId);

        Name = name.Trim();
        CostType = costType;
        CostDetailType = costDetailType;
        CarrierId = normalizedCarrierId;
        CarrierName = normalizedCarrierId.HasValue ? Normalize(carrierName) : null;
        CarrierCode = normalizedCarrierId.HasValue ? Normalize(carrierCode) : null;
        AgentId = normalizedAgentId;
        AgentName = normalizedAgentId.HasValue ? Normalize(agentName) : null;
        AgentCode = normalizedAgentId.HasValue ? Normalize(agentCode) : null;
        PortId = normalizedPortId;
        PortName = normalizedPortId.HasValue ? Normalize(portName) : null;
        PortCode = normalizedPortId.HasValue ? Normalize(portCode) : null;
        PortRole = normalizedPortId.HasValue ? portRole : null;
        CurrencyId = currencyId;
        CurrencyName = currencyName.Trim();
        CurrencyCode = currencyCode.Trim();
        CostAmount = costAmount;
        SaleAmount = saleAmount;
        UtilityAmount = saleAmount - costAmount;
        Notes = Normalize(notes);
        // El flete marítimo y el transporte terrestre siempre se aplican por contenedor.
        // La bandera continúa siendo configurable para los demás rubros.
        IsAccountant = isAccountant || IsFreightPerContainer(costDetailType);
    }

    public void Delete(Guid? deletedBy)
    {
        MarkAsDeleted(DateTime.UtcNow, deletedBy?.ToString());
        AddDomainEvent(new CostDeletedDomainEvent(Id, Name, deletedBy));
    }

    public void SetActive(bool isActive, Guid? updatedBy = null)
    {
        if (IsActive == isActive)
            return;

        IsActive = isActive;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
        AddDomainEvent(
            IsActive
                ? new CostActivatedDomainEvent(Id, Name, updatedBy)
                : new CostInactivatedDomainEvent(Id, Name, updatedBy)
        );
    }

    private static bool IsFreightPerContainer(CostDetailType costDetailType) =>
        costDetailType is CostDetailType.Freight or CostDetailType.InlandTransport;

    private static Guid? NormalizeId(Guid? value) => value.HasValue && value.Value != Guid.Empty ? value : null;
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
