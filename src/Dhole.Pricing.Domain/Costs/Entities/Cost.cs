using CustomCodeFramework.Core.Domain.Entities;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Costs.Events;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Domain.Costs.Entities;

public sealed class Cost : SoftDeletableAggregateRoot<Guid>
{
    private readonly List<CostIncoterm> _incoterms = [];

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
        Guid? polId,
        string? polName,
        string? polCode,
        Guid? poeId,
        string? poeName,
        string? poeCode,
        Guid? podId,
        string? podName,
        string? podCode,
        IReadOnlyCollection<CostIncotermSelection>? incoterms,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes,
        bool isAccountant,
        ShipmentMode? shipmentMode,
        ChargeBasis chargeBasis,
        decimal? minimumCostAmount,
        decimal? minimumSaleAmount,
        decimal? kgPerCbm,
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
            polId,
            polName,
            polCode,
            poeId,
            poeName,
            poeCode,
            podId,
            podName,
            podCode,
            incoterms,
            currencyId,
            currencyName,
            currencyCode,
            costAmount,
            saleAmount,
            notes,
            isAccountant,
            shipmentMode,
            chargeBasis,
            minimumCostAmount,
            minimumSaleAmount,
            kgPerCbm
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

    // Route-specific conditions. Any populated field becomes a required match.
    // Legacy PortId/PortRole remains supported for existing records and "Any point".
    public Guid? PolId { get; private set; }
    public string? PolName { get; private set; }
    public string? PolCode { get; private set; }
    public Guid? PoeId { get; private set; }
    public string? PoeName { get; private set; }
    public string? PoeCode { get; private set; }
    public Guid? PodId { get; private set; }
    public string? PodName { get; private set; }
    public string? PodCode { get; private set; }

    public IReadOnlyCollection<CostIncoterm> Incoterms => _incoterms.AsReadOnly();
    public Guid CurrencyId { get; private set; }
    public string CurrencyName { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal CostAmount { get; private set; }
    public decimal SaleAmount { get; private set; }
    public decimal UtilityAmount { get; private set; }
    public ShipmentMode? ShipmentMode { get; private set; }
    public ChargeBasis ChargeBasis { get; private set; } = ChargeBasis.PerShipment;
    public decimal? MinimumCostAmount { get; private set; }
    public decimal? MinimumSaleAmount { get; private set; }
    public decimal? KgPerCbm { get; private set; }
    public string? Notes { get; private set; }
    private bool _isAccountant;
    public bool IsAccountant
    {
        get => _isAccountant || ChargeBasis is ChargeBasis.PerContainer or ChargeBasis.PerTruck;
        private set => _isAccountant = value;
    }
    public bool IsActive { get; private set; }

    // Backwards-compatible factory for existing FCL callers. New callers should use the
    // overload that explicitly supplies ShipmentMode and ChargeBasis.
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
        Guid? polId,
        string? polName,
        string? polCode,
        Guid? poeId,
        string? poeName,
        string? poeCode,
        Guid? podId,
        string? podName,
        string? podCode,
        IReadOnlyCollection<CostIncotermSelection>? incoterms,
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
        var perEquipment = IsFreightPerContainer(costDetailType) || isAccountant;
        return Create(
            name, costType, costDetailType,
            carrierId, carrierName, carrierCode,
            agentId, agentName, agentCode,
            portId, portName, portCode, portRole,
            polId, polName, polCode,
            poeId, poeName, poeCode,
            podId, podName, podCode,
            incoterms, currencyId, currencyName, currencyCode, costAmount, saleAmount, notes,
            isAccountant,
            perEquipment
                ? Dhole.Pricing.Domain.Rates.Enums.ShipmentMode.Fcl
                : (Dhole.Pricing.Domain.Rates.Enums.ShipmentMode?)null,
            perEquipment
                ? Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerContainer
                : costDetailType == CostDetailType.Documentation
                    ? Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerDocument
                    : Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerShipment,
            minimumCostAmount: null, minimumSaleAmount: null, kgPerCbm: null, createdBy: createdBy
        );
    }

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
        Guid? polId,
        string? polName,
        string? polCode,
        Guid? poeId,
        string? poeName,
        string? poeCode,
        Guid? podId,
        string? podName,
        string? podCode,
        IReadOnlyCollection<CostIncotermSelection>? incoterms,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes,
        bool isAccountant,
        ShipmentMode? shipmentMode,
        ChargeBasis chargeBasis,
        decimal? minimumCostAmount,
        decimal? minimumSaleAmount,
        decimal? kgPerCbm,
        Guid? createdBy
    )
    {
        var cost = new Cost(
            Guid.NewGuid(), name, costType, costDetailType,
            carrierId, carrierName, carrierCode,
            agentId, agentName, agentCode,
            portId, portName, portCode, portRole,
            polId, polName, polCode,
            poeId, poeName, poeCode,
            podId, podName, podCode,
            incoterms,
            currencyId, currencyName, currencyCode,
            costAmount, saleAmount, notes, isAccountant, shipmentMode, chargeBasis,
            minimumCostAmount, minimumSaleAmount, kgPerCbm, createdBy
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
        Guid? polId,
        string? polName,
        string? polCode,
        Guid? poeId,
        string? poeName,
        string? poeCode,
        Guid? podId,
        string? podName,
        string? podCode,
        IReadOnlyCollection<CostIncotermSelection>? incoterms,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes,
        bool isAccountant,
        ShipmentMode? shipmentMode,
        ChargeBasis chargeBasis,
        decimal? minimumCostAmount,
        decimal? minimumSaleAmount,
        decimal? kgPerCbm,
        Guid? updatedBy
    )
    {
        Apply(
            name, costType, costDetailType,
            carrierId, carrierName, carrierCode,
            agentId, agentName, agentCode,
            portId, portName, portCode, portRole,
            polId, polName, polCode,
            poeId, poeName, poeCode,
            podId, podName, podCode,
            incoterms,
            currencyId, currencyName, currencyCode,
            costAmount, saleAmount, notes, isAccountant, shipmentMode, chargeBasis,
            minimumCostAmount, minimumSaleAmount, kgPerCbm
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
        Guid? polId,
        string? polName,
        string? polCode,
        Guid? poeId,
        string? poeName,
        string? poeCode,
        Guid? podId,
        string? podName,
        string? podCode,
        IReadOnlyCollection<CostIncotermSelection>? incoterms,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes,
        bool isAccountant,
        ShipmentMode? shipmentMode,
        ChargeBasis chargeBasis,
        decimal? minimumCostAmount,
        decimal? minimumSaleAmount,
        decimal? kgPerCbm
    )
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("El nombre del costo es obligatorio.");
        if (currencyId == Guid.Empty || string.IsNullOrWhiteSpace(currencyName) || string.IsNullOrWhiteSpace(currencyCode))
            throw new InvalidOperationException("La moneda del costo es obligatoria.");
        if (costAmount < 0m || saleAmount < 0m)
            throw new InvalidOperationException("Los montos del costo no pueden ser negativos.");
        if (minimumCostAmount is < 0m || minimumSaleAmount is < 0m)
            throw new InvalidOperationException("Los mínimos del costo no pueden ser negativos.");
        if (kgPerCbm is <= 0m)
            throw new InvalidOperationException("El factor KG/CBM debe ser mayor que cero.");

        var normalizedCarrierId = NormalizeId(carrierId);
        var normalizedAgentId = NormalizeId(agentId);
        var normalizedPortId = NormalizeId(portId);
        var normalizedPolId = NormalizeId(polId);
        var normalizedPoeId = NormalizeId(poeId);
        var normalizedPodId = NormalizeId(podId);
        var hasStructuredRoute =
            normalizedPolId.HasValue || normalizedPoeId.HasValue || normalizedPodId.HasValue;

        Name = name.Trim();
        CostType = costType;
        CostDetailType = costDetailType;
        CarrierId = normalizedCarrierId;
        CarrierName = normalizedCarrierId.HasValue ? Normalize(carrierName) : null;
        CarrierCode = normalizedCarrierId.HasValue ? Normalize(carrierCode) : null;
        AgentId = normalizedAgentId;
        AgentName = normalizedAgentId.HasValue ? Normalize(agentName) : null;
        AgentCode = normalizedAgentId.HasValue ? Normalize(agentCode) : null;
        // Structured route conditions take precedence over the legacy single-port scope.
        PortId = hasStructuredRoute ? null : normalizedPortId;
        PortName = PortId.HasValue ? Normalize(portName) : null;
        PortCode = PortId.HasValue ? Normalize(portCode) : null;
        PortRole = PortId.HasValue ? portRole : null;

        PolId = normalizedPolId;
        PolName = normalizedPolId.HasValue ? Normalize(polName) : null;
        PolCode = normalizedPolId.HasValue ? Normalize(polCode) : null;
        PoeId = normalizedPoeId;
        PoeName = normalizedPoeId.HasValue ? Normalize(poeName) : null;
        PoeCode = normalizedPoeId.HasValue ? Normalize(poeCode) : null;
        PodId = normalizedPodId;
        PodName = normalizedPodId.HasValue ? Normalize(podName) : null;
        PodCode = normalizedPodId.HasValue ? Normalize(podCode) : null;

        ReplaceIncoterms(incoterms);
        CurrencyId = currencyId;
        CurrencyName = currencyName.Trim();
        CurrencyCode = currencyCode.Trim();
        CostAmount = costAmount;
        SaleAmount = saleAmount;
        UtilityAmount = saleAmount - costAmount;
        ShipmentMode = shipmentMode;
        ChargeBasis = chargeBasis;
        MinimumCostAmount = minimumCostAmount;
        MinimumSaleAmount = minimumSaleAmount;
        KgPerCbm = kgPerCbm;
        Notes = Normalize(notes);
        IsAccountant = isAccountant || chargeBasis is ChargeBasis.PerContainer or ChargeBasis.PerTruck;
    }

    private void ReplaceIncoterms(IReadOnlyCollection<CostIncotermSelection>? incoterms)
    {
        var selections = incoterms ?? [];
        var normalized = selections
            .Where(x => x.Id != Guid.Empty)
            .GroupBy(x => x.Id)
            .Select(group => group.First())
            .ToArray();

        if (normalized.Length != selections.Count)
            throw new InvalidOperationException("Los Incoterms del costo no pueden estar vacíos ni repetidos.");

        var selectedIds = normalized.Select(x => x.Id).ToHashSet();
        _incoterms.RemoveAll(x => !selectedIds.Contains(x.IncotermId));

        foreach (var incoterm in normalized)
        {
            var existing = _incoterms.FirstOrDefault(x => x.IncotermId == incoterm.Id);
            if (existing is null)
            {
                _incoterms.Add(new CostIncoterm(Id, incoterm.Id, incoterm.Name, incoterm.Code));
                continue;
            }

            existing.UpdateSnapshot(incoterm.Name, incoterm.Code);
        }
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
