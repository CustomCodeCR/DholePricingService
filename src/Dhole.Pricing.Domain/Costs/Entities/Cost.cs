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
        Guid portId,
        string portName,
        string portCode,
        CostPortRole portRole,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes,
        Guid? createdBy
    )
        : base(id)
    {
        Name = name;
        CostType = costType;
        CostDetailType = costDetailType;
        CarrierId = carrierId;
        CarrierName = carrierName;
        CarrierCode = carrierCode;
        AgentId = agentId;
        AgentName = agentName;
        AgentCode = agentCode;
        PortId = portId;
        PortName = portName;
        PortCode = portCode;
        PortRole = portRole;
        CurrencyId = currencyId;
        CurrencyName = currencyName;
        CurrencyCode = currencyCode;
        CostAmount = costAmount;
        SaleAmount = saleAmount;

        UtilityAmount = saleAmount - costAmount;
        Notes = notes;

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

    public Guid PortId { get; private set; }
    public string PortName { get; private set; } = string.Empty;
    public string PortCode { get; private set; } = string.Empty;

    public CostPortRole PortRole { get; private set; }

    public Guid CurrencyId { get; private set; }
    public string CurrencyName { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;

    public decimal CostAmount { get; private set; }
    public decimal SaleAmount { get; private set; }
    public decimal UtilityAmount { get; private set; }

    public string? Notes { get; private set; }

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
        Guid portId,
        string portName,
        string portCode,
        CostPortRole portRole,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes,
        Guid? createdBy
    )
    {
        var cost = new Cost(
            Guid.NewGuid(),
            name,
            costType,
            costDetailType,
            carrierId,
            carrierName,
            carrierCode,
            agentId,
            agentName!,
            agentCode!,
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
            createdBy
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
        Guid portId,
        string portName,
        string portCode,
        CostPortRole portRole,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes,
        Guid? updatedBy
    )
    {
        Name = name;
        CostType = costType;
        CostDetailType = costDetailType;
        CarrierId = carrierId;
        CarrierName = carrierName;
        CarrierCode = carrierCode;
        AgentId = agentId;
        AgentName = agentName;
        AgentCode = agentCode;
        PortId = portId;
        PortName = portName;
        PortCode = portCode;
        PortRole = portRole;
        CurrencyId = currencyId;
        CurrencyName = currencyName;
        CurrencyCode = currencyCode;
        CostAmount = costAmount;
        SaleAmount = saleAmount;
        UtilityAmount = saleAmount - costAmount;
        Notes = notes;

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());

        AddDomainEvent(new CostUpdatedDomainEvent(Id, Name, updatedBy));
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
}
