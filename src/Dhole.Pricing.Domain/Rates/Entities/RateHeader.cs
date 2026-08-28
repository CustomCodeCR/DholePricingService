using CustomCodeFramework.Core.Domain.Entities;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Rates.Events;

namespace Dhole.Pricing.Domain.Rates.Entities;

public sealed class RateHeader : SoftDeletableAggregateRoot<Guid>
{
    private const decimal MinimumMarginPercentage = 12m;
    private readonly List<RateDetail> _rateDetails = [];
    private readonly List<RateContainerAllocation> _rateContainers = [];

    private RateHeader() { }

    private RateHeader(
        Guid id,
        string rateCode,
        Guid? sourceImportFclRateId,
        Guid? agentId,
        string? agentName,
        string? agentCode,
        Guid? carrierId,
        string? carrierName,
        string? carrierCode,
        Guid polId,
        string polName,
        string polCode,
        Guid poeId,
        string poeName,
        string poeCode,
        Guid? podId,
        string? podName,
        string? podCode,
        Guid containerTypeId,
        string containerTypeName,
        string containerTypeCode,
        Guid? incotermId,
        string? incotermName,
        string? incotermCode,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        int freeDays,
        DateTime validFrom,
        DateTime validTo,
        int containerQuantity,
        string? clientName,
        string? idtraNumber,
        string? quoNumber,
        string? includes,
        string? subjectTo,
        string? excludes,
        string? transitTime,
        RateType rateType,
        Guid? createdBy
    )
        : base(id)
    {
        ValidateHeader(
            agentId,
            agentName,
            agentCode,
            carrierId,
            carrierName,
            carrierCode,
            polId,
            polName,
            polCode,
            poeId,
            poeName,
            poeCode,
            podId,
            podName,
            podCode,
            containerTypeId,
            containerTypeName,
            containerTypeCode,
            incotermId,
            incotermName,
            incotermCode,
            currencyId,
            currencyName,
            currencyCode,
            freeDays,
            validFrom,
            validTo,
            containerQuantity
        );

        ValidateRateTerms(includes, subjectTo, excludes);

        SourceImportFclRateId = sourceImportFclRateId;
        AgentId = agentId;
        AgentName = agentName?.Trim();
        AgentCode = agentCode?.Trim();
        CarrierId = carrierId;
        CarrierName = carrierName?.Trim();
        CarrierCode = carrierCode?.Trim();
        PolId = polId;
        PolName = polName.Trim();
        PolCode = polCode.Trim();
        PoeId = poeId;
        PoeName = poeName.Trim();
        PoeCode = poeCode.Trim();
        PodId = NormalizeId(podId);
        PodName = PodId.HasValue ? Normalize(podName) : null;
        PodCode = PodId.HasValue ? Normalize(podCode) : null;
        ContainerTypeId = containerTypeId;
        ContainerTypeName = containerTypeName.Trim();
        ContainerTypeCode = containerTypeCode.Trim();
        IncotermId = NormalizeId(incotermId);
        IncotermName = IncotermId.HasValue ? Normalize(incotermName) : null;
        IncotermCode = IncotermId.HasValue ? Normalize(incotermCode) : null;
        CurrencyId = currencyId;
        CurrencyName = currencyName.Trim();
        CurrencyCode = currencyCode.Trim();
        FreeDays = freeDays;
        ValidFrom = validFrom;
        ValidTo = validTo;
        ContainerQuantity = containerQuantity;
        _rateContainers.Add(
            RateContainerAllocation.Create(
                Id,
                containerTypeId,
                containerTypeName,
                containerTypeCode,
                containerQuantity
            )
        );
        ClientName = Normalize(clientName);
        IdtraNumber = Normalize(idtraNumber);
        QuoNumber = Normalize(quoNumber);
        Includes = Normalize(includes);
        SubjectTo = Normalize(subjectTo);
        Excludes = Normalize(excludes);
        TransitTime = Normalize(transitTime);
        RateType = rateType;
        RateCode = ValidateRateCode(rateCode);
        RateName = CreateRateName(
            RateCode,
            BuildContainerDescription(),
            PolName,
            PoeName,
            PodName,
            IncotermName ?? IncotermCode,
            ClientName
        );

        Status = RateStatus.PendingApproval;
        RequiredApproval = true;
        MarkAsCreated(DateTime.UtcNow, createdBy?.ToString());
    }

    public Guid? SourceImportFclRateId { get; private set; }

    public Guid? AgentId { get; private set; }
    public string? AgentName { get; private set; } = string.Empty;
    public string? AgentCode { get; private set; } = string.Empty;

    public Guid? CarrierId { get; private set; }
    public string? CarrierName { get; private set; } = string.Empty;
    public string? CarrierCode { get; private set; } = string.Empty;

    public Guid PolId { get; private set; }
    public string PolName { get; private set; } = string.Empty;
    public string PolCode { get; private set; } = string.Empty;

    public Guid PoeId { get; private set; }
    public string PoeName { get; private set; } = string.Empty;
    public string PoeCode { get; private set; } = string.Empty;

    public Guid? PodId { get; private set; }
    public string? PodName { get; private set; }
    public string? PodCode { get; private set; }

    public Guid ContainerTypeId { get; private set; }
    public string ContainerTypeName { get; private set; } = string.Empty;
    public string ContainerTypeCode { get; private set; } = string.Empty;

    public Guid? IncotermId { get; private set; }
    public string? IncotermName { get; private set; }
    public string? IncotermCode { get; private set; }

    public string? PickupAddress { get; private set; }
    public decimal? PickupLatitude { get; private set; }
    public decimal? PickupLongitude { get; private set; }

    public Guid CurrencyId { get; private set; }
    public string CurrencyName { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;

    public int FreeDays { get; private set; }
    public DateTime ValidFrom { get; private set; }
    public DateTime ValidTo { get; private set; }

    public string RateCode { get; private set; } = string.Empty;
    public string RateName { get; private set; } = string.Empty;
    public int ContainerQuantity { get; private set; }

    public ShipmentMode ShipmentMode { get; private set; } = ShipmentMode.Fcl;
    public int TotalPackages { get; private set; }
    public int TotalPallets { get; private set; }
    public decimal TotalWeightKg { get; private set; }
    public decimal TotalVolumeCbm { get; private set; }
    public decimal KgPerCbm { get; private set; } = 500m;
    public decimal ChargeableQuantity { get; private set; } = 1m;
    public string? CargoLinesJson { get; private set; }

    public string? ClientName { get; private set; }
    public string? ExecutiveName { get; private set; }
    public string? IdtraNumber { get; private set; }
    public string? QuoNumber { get; private set; }
    public string? Includes { get; private set; }
    public string? SubjectTo { get; private set; }
    public string? Excludes { get; private set; }
    public string? TransitTime { get; private set; }
    public RateType RateType { get; private set; } = RateType.Tariff;

    public decimal TotalCostAmount { get; private set; }
    public decimal TotalSaleAmount { get; private set; }
    public decimal TotalUtilityAmount { get; private set; }
    public decimal MarginPercentage { get; private set; }
    public bool RequiredApproval { get; private set; }
    public RateStatus Status { get; private set; }
    public string? ClosedReason { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public Guid? ClosedBy { get; private set; }

    public IReadOnlyCollection<RateDetail> RateDetails => _rateDetails.AsReadOnly();
    public IReadOnlyCollection<RateContainerAllocation> RateContainers => _rateContainers.AsReadOnly();

    public static RateHeader Create(
        string rateCode,
        Guid? sourceImportFclRateId,
        Guid? agentId,
        string? agentName,
        string? agentCode,
        Guid? carrierId,
        string? carrierName,
        string? carrierCode,
        Guid polId,
        string polName,
        string polCode,
        Guid poeId,
        string poeName,
        string poeCode,
        Guid? podId,
        string? podName,
        string? podCode,
        Guid containerTypeId,
        string containerTypeName,
        string containerTypeCode,
        Guid? incotermId,
        string? incotermName,
        string? incotermCode,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        int freeDays,
        DateTime validFrom,
        DateTime validTo,
        int containerQuantity,
        string? clientName,
        string? idtraNumber,
        string? quoNumber,
        string? includes,
        string? subjectTo,
        string? excludes,
        string? transitTime,
        RateType rateType,
        Guid? createdBy
    )
    {
        var rate = new RateHeader(
            Guid.NewGuid(),
            rateCode,
            sourceImportFclRateId,
            agentId,
            agentName,
            agentCode,
            carrierId,
            carrierName,
            carrierCode,
            polId,
            polName,
            polCode,
            poeId,
            poeName,
            poeCode,
            podId,
            podName,
            podCode,
            containerTypeId,
            containerTypeName,
            containerTypeCode,
            incotermId,
            incotermName,
            incotermCode,
            currencyId,
            currencyName,
            currencyCode,
            freeDays,
            validFrom,
            validTo,
            containerQuantity,
            clientName,
            idtraNumber,
            quoNumber,
            includes,
            subjectTo,
            excludes,
            transitTime,
            rateType,
            createdBy
        );

        rate.AddDomainEvent(new RateHeaderCreatedDomainEvent(rate.Id, createdBy));
        return rate;
    }

    public void Update(
        Guid agentId,
        string agentName,
        string agentCode,
        Guid carrierId,
        string carrierName,
        string carrierCode,
        Guid polId,
        string polName,
        string polCode,
        Guid poeId,
        string poeName,
        string poeCode,
        Guid? podId,
        string? podName,
        string? podCode,
        Guid containerTypeId,
        string containerTypeName,
        string containerTypeCode,
        Guid? incotermId,
        string? incotermName,
        string? incotermCode,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        int freeDays,
        DateTime validFrom,
        DateTime validTo,
        int containerQuantity,
        string? clientName,
        string? idtraNumber,
        string? quoNumber,
        string? includes,
        string? subjectTo,
        string? excludes,
        string? transitTime,
        RateType rateType,
        Guid? updatedBy
    )
    {
        ValidateHeader(
            agentId,
            agentName,
            agentCode,
            carrierId,
            carrierName,
            carrierCode,
            polId,
            polName,
            polCode,
            poeId,
            poeName,
            poeCode,
            podId,
            podName,
            podCode,
            containerTypeId,
            containerTypeName,
            containerTypeCode,
            incotermId,
            incotermName,
            incotermCode,
            currencyId,
            currencyName,
            currencyCode,
            freeDays,
            validFrom,
            validTo,
            containerQuantity
        );

        ValidateRateTerms(includes, subjectTo, excludes);

        AgentId = agentId;
        AgentName = agentName.Trim();
        AgentCode = agentCode.Trim();
        CarrierId = carrierId;
        CarrierName = carrierName.Trim();
        CarrierCode = carrierCode.Trim();
        PolId = polId;
        PolName = polName.Trim();
        PolCode = polCode.Trim();
        PoeId = poeId;
        PoeName = poeName.Trim();
        PoeCode = poeCode.Trim();
        PodId = NormalizeId(podId);
        PodName = PodId.HasValue ? Normalize(podName) : null;
        PodCode = PodId.HasValue ? Normalize(podCode) : null;
        ContainerTypeId = containerTypeId;
        ContainerTypeName = containerTypeName.Trim();
        ContainerTypeCode = containerTypeCode.Trim();
        IncotermId = NormalizeId(incotermId);
        IncotermName = IncotermId.HasValue ? Normalize(incotermName) : null;
        IncotermCode = IncotermId.HasValue ? Normalize(incotermCode) : null;
        CurrencyId = currencyId;
        CurrencyName = currencyName.Trim();
        CurrencyCode = currencyCode.Trim();
        FreeDays = freeDays;
        ValidFrom = validFrom;
        ValidTo = validTo;
        ContainerQuantity = containerQuantity;
        SynchronizeFreightQuantities();
        ClientName = Normalize(clientName);
        IdtraNumber = Normalize(idtraNumber);
        QuoNumber = Normalize(quoNumber);
        Includes = Normalize(includes);
        SubjectTo = Normalize(subjectTo);
        Excludes = Normalize(excludes);
        TransitTime = Normalize(transitTime);
        RateType = rateType;

        RateName = CreateRateName(
            RateCode,
            BuildContainerDescription(),
            PolName,
            PoeName,
            PodName,
            IncotermName ?? IncotermCode,
            ClientName
        );

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
        AddDomainEvent(new RateHeaderUpdatedDomainEvent(Id, updatedBy));
    }

    public void ConfigurePickupLocation(
        string? pickupAddress,
        decimal? pickupLatitude,
        decimal? pickupLongitude
    )
    {
        var applies = string.Equals(IncotermCode, "EXW", StringComparison.OrdinalIgnoreCase)
            || string.Equals(IncotermCode, "FCA", StringComparison.OrdinalIgnoreCase);

        if (!applies)
        {
            PickupAddress = null;
            PickupLatitude = null;
            PickupLongitude = null;
            return;
        }

        if (pickupLatitude is < -90m or > 90m)
            throw new InvalidOperationException("La latitud de recolección no es válida.");
        if (pickupLongitude is < -180m or > 180m)
            throw new InvalidOperationException("La longitud de recolección no es válida.");

        PickupAddress = Normalize(pickupAddress);
        PickupLatitude = pickupLatitude;
        PickupLongitude = pickupLongitude;
    }

    public void ConfigureExecutive(string? executiveName)
    {
        // El ejecutivo comercial es editable por Pricing hasta nuevo aviso.
        // No se deriva del usuario autenticado ni se bloquea contra Auth.
        ExecutiveName = Normalize(executiveName);
    }

    public void ReplaceContainerAllocations(
        IReadOnlyCollection<RateContainerAllocationSpec> containers,
        Guid? updatedBy
    )
    {
        ReplaceContainerAllocationsInternal(containers, refreshRateName: true);
        SynchronizeFreightQuantities();
    }

    private void ReplaceContainerAllocationsInternal(
        IReadOnlyCollection<RateContainerAllocationSpec> containers,
        bool refreshRateName
    )
    {
        if (containers is null || containers.Count == 0)
        {
            throw new InvalidOperationException(
                "La tarifa debe contener al menos un tipo de contenedor."
            );
        }

        var normalized = containers
            .Select(x => new RateContainerAllocationSpec(
                x.ContainerTypeId,
                x.ContainerTypeName?.Trim() ?? string.Empty,
                x.ContainerTypeCode?.Trim() ?? string.Empty,
                x.Quantity
            ))
            .ToArray();

        if (normalized.Any(x =>
            x.ContainerTypeId == Guid.Empty
            || string.IsNullOrWhiteSpace(x.ContainerTypeName)
            || string.IsNullOrWhiteSpace(x.ContainerTypeCode)
            || x.Quantity <= 0))
        {
            throw new InvalidOperationException(
                "Cada tipo de contenedor debe tener catálogo válido y cantidad mayor que cero."
            );
        }

        if (normalized.Select(x => x.ContainerTypeId).Distinct().Count() != normalized.Length)
        {
            throw new InvalidOperationException(
                "Un tipo de contenedor no puede repetirse dentro de la misma tarifa."
            );
        }

        var totalQuantity = normalized.Sum(x => x.Quantity);
        if (totalQuantity <= 0)
        {
            throw new InvalidOperationException(
                "La cantidad total de contenedores debe ser mayor que cero."
            );
        }

        var requestedIds = normalized.Select(x => x.ContainerTypeId).ToHashSet();
        _rateContainers.RemoveAll(x => !requestedIds.Contains(x.ContainerTypeId));

        foreach (var item in normalized)
        {
            var existing = _rateContainers.FirstOrDefault(x =>
                x.ContainerTypeId == item.ContainerTypeId
            );
            if (existing is not null)
            {
                existing.Update(item.ContainerTypeName, item.ContainerTypeCode, item.Quantity);
                continue;
            }

            _rateContainers.Add(
                RateContainerAllocation.Create(
                    Id,
                    item.ContainerTypeId,
                    item.ContainerTypeName,
                    item.ContainerTypeCode,
                    item.Quantity
                )
            );
        }

        var primary = normalized[0];
        ContainerTypeId = primary.ContainerTypeId;
        ContainerTypeName = primary.ContainerTypeName;
        ContainerTypeCode = primary.ContainerTypeCode;
        ContainerQuantity = totalQuantity;
        if (ShipmentMode is ShipmentMode.Fcl or ShipmentMode.Ftl)
            ChargeableQuantity = Math.Max(ContainerQuantity, 1);

        if (refreshRateName)
        {
            RateName = CreateRateName(
                RateCode,
                BuildShipmentDescription(),
                PolName,
                PoeName,
                PodName,
                IncotermName ?? IncotermCode,
                ClientName
            );
        }
    }

    public void ConfigureShipment(
        ShipmentMode shipmentMode,
        int totalPackages,
        int totalPallets,
        decimal totalWeightKg,
        decimal totalVolumeCbm,
        decimal kgPerCbm,
        string? cargoLinesJson,
        Guid? updatedBy
    )
    {
        if (totalPackages < 0 || totalPallets < 0 || totalWeightKg < 0m || totalVolumeCbm < 0m)
            throw new InvalidOperationException("Las métricas de carga no pueden ser negativas.");

        if (shipmentMode is ShipmentMode.Lcl or ShipmentMode.Ltl && kgPerCbm <= 0m)
            throw new InvalidOperationException("El factor KG/CBM debe ser mayor que cero para LCL/LTL.");

        ShipmentMode = shipmentMode;
        TotalPackages = totalPackages;
        TotalPallets = totalPallets;
        TotalWeightKg = totalWeightKg;
        TotalVolumeCbm = totalVolumeCbm;
        KgPerCbm = kgPerCbm > 0m ? kgPerCbm : 500m;
        CargoLinesJson = Normalize(cargoLinesJson);
        ChargeableQuantity = shipmentMode switch
        {
            ShipmentMode.Lcl or ShipmentMode.Ltl => Math.Max(TotalVolumeCbm, TotalWeightKg / KgPerCbm),
            ShipmentMode.Ftl or ShipmentMode.Fcl => Math.Max(ContainerQuantity, 1),
            _ => 1m,
        };

        if (shipmentMode is ShipmentMode.Lcl or ShipmentMode.Ltl && ChargeableQuantity <= 0m)
            throw new InvalidOperationException("La carga LCL/LTL debe tener peso o volumen cobrable.");

        RateName = CreateRateName(
            RateCode,
            BuildShipmentDescription(),
            PolName,
            PoeName,
            PodName,
            IncotermName ?? IncotermCode,
            ClientName
        );

        SynchronizeFreightQuantities();
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public decimal ResolveChargeQuantity(
        ChargeBasis chargeBasis,
        decimal requestedQuantity = 0m,
        decimal? kgPerCbmOverride = null
    )
    {
        var explicitQuantity = requestedQuantity > 0m ? requestedQuantity : 1m;
        var chargeableCbm = kgPerCbmOverride is > 0m
            ? Math.Max(TotalVolumeCbm, TotalWeightKg / kgPerCbmOverride.Value)
            : ChargeableQuantity;

        return chargeBasis switch
        {
            // Per-container/per-truck details may represent only one equipment type (for example
            // a 40 HC freight row inside a mixed FCL rate). When a quantity is supplied, preserve it.
            // Callers that need the whole shipment can omit requestedQuantity.
            ChargeBasis.PerContainer => requestedQuantity > 0m ? requestedQuantity : Math.Max(ContainerQuantity, 1),
            ChargeBasis.PerTruck => requestedQuantity > 0m ? requestedQuantity : Math.Max(ContainerQuantity, 1),
            ChargeBasis.PerTeu => ResolveTeuQuantity(requestedQuantity),
            ChargeBasis.PerCbm => Math.Max(TotalVolumeCbm, 0.001m),
            ChargeBasis.PerChargeableCbm => Math.Max(chargeableCbm, 0.001m),
            ChargeBasis.PerKg => Math.Max(TotalWeightKg, 0.001m),
            ChargeBasis.Per100Kg => Math.Max(TotalWeightKg / 100m, 0.001m),
            ChargeBasis.PerTon => Math.Max(TotalWeightKg / 1000m, 0.001m),
            ChargeBasis.PerPallet => Math.Max(TotalPallets, 1),
            ChargeBasis.PerPackage => Math.Max(TotalPackages, 1),
            ChargeBasis.PerDocument => explicitQuantity,
            _ => 1m,
        };
    }

    private decimal ResolveTeuQuantity(decimal requestedQuantity)
    {
        if (requestedQuantity > 0m)
            return requestedQuantity;

        if (_rateContainers.Count > 0)
        {
            return _rateContainers.Sum(container =>
            {
                var equipment = $"{container.ContainerTypeCode} {container.ContainerTypeName}";
                var isTwentyFoot = System.Text.RegularExpressions.Regex.IsMatch(
                    equipment,
                    @"(^|\D)20(\D|$)",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                return container.Quantity * (isTwentyFoot ? 1m : 2m);
            });
        }

        var headerEquipment = $"{ContainerTypeCode} {ContainerTypeName}";
        var headerIsTwentyFoot = System.Text.RegularExpressions.Regex.IsMatch(
            headerEquipment,
            @"(^|\D)20(\D|$)",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return Math.Max(ContainerQuantity, 1) * (headerIsTwentyFoot ? 1m : 2m);
    }

    private ChargeBasis InferChargeBasis(CostDetailType costDetailType)
    {
        if (costDetailType == CostDetailType.Documentation)
            return ChargeBasis.PerDocument;

        if (costDetailType is not (CostDetailType.Freight or CostDetailType.InlandTransport))
            return ChargeBasis.PerShipment;

        return ShipmentMode switch
        {
            ShipmentMode.Fcl => ChargeBasis.PerContainer,
            ShipmentMode.Ftl => ChargeBasis.PerTruck,
            ShipmentMode.Lcl or ShipmentMode.Ltl => ChargeBasis.PerChargeableCbm,
            _ => ChargeBasis.PerShipment,
        };
    }

    public RateDetail AddRateDetail(
        Guid rateHeaderId,
        Guid? costId,
        string name,
        CostDetailType costDetailType,
        CostType costType,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes,
        int quantity,
        Guid? updatedBy
    )
    {
        if (rateHeaderId != Id)
        {
            throw new InvalidOperationException("El detalle no corresponde a la tarifa.");
        }

        ValidateDetail(name, currencyId, currencyName, currencyCode, costAmount, saleAmount);

        var chargeBasis = InferChargeBasis(costDetailType);
        var effectiveQuantity = costDetailType == CostDetailType.InlandTransport
            ? ResolveChargeQuantity(chargeBasis)
            : ResolveChargeQuantity(chargeBasis, quantity);

        var detail = RateDetail.Create(
            Id,
            costId,
            name.Trim(),
            costDetailType,
            costType,
            chargeBasis,
            currencyId,
            currencyName.Trim(),
            currencyCode.Trim(),
            costAmount,
            saleAmount,
            Normalize(notes),
            effectiveQuantity
        );

        _rateDetails.Add(detail);
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
        return detail;
    }

    public RateDetail AddRateDetail(
        Guid rateHeaderId,
        Guid? costId,
        string name,
        CostDetailType costDetailType,
        CostType costType,
        ChargeBasis chargeBasis,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes,
        decimal quantity,
        Guid? updatedBy
    )
    {
        if (rateHeaderId != Id)
            throw new InvalidOperationException("El detalle no corresponde a la tarifa.");

        ValidateDetail(name, currencyId, currencyName, currencyCode, costAmount, saleAmount);
        var effectiveQuantity = ResolveChargeQuantity(chargeBasis, quantity);
        var detail = RateDetail.Create(
            Id, costId, name.Trim(), costDetailType, costType, chargeBasis, currencyId,
            currencyName.Trim(), currencyCode.Trim(), costAmount, saleAmount, Normalize(notes),
            effectiveQuantity
        );
        _rateDetails.Add(detail);
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
        return detail;
    }

    public void UpdateRateDetail(
        Guid rateDetailId,
        Guid? costId,
        string name,
        CostDetailType costDetailType,
        CostType costType,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes,
        int quantity,
        Guid? updatedBy
    )
    {
        var detail = _rateDetails.FirstOrDefault(x => x.Id == rateDetailId);

        if (detail is null)
        {
            throw new InvalidOperationException("El detalle de la tarifa no existe.");
        }

        ValidateDetail(name, currencyId, currencyName, currencyCode, costAmount, saleAmount);

        var chargeBasis = InferChargeBasis(costDetailType);
        var effectiveQuantity = costDetailType == CostDetailType.InlandTransport
            ? ResolveChargeQuantity(chargeBasis)
            : ResolveChargeQuantity(chargeBasis, quantity);

        detail.Update(
            costId,
            name.Trim(),
            costDetailType,
            costType,
            chargeBasis,
            currencyId,
            currencyName.Trim(),
            currencyCode.Trim(),
            costAmount,
            saleAmount,
            Normalize(notes),
            effectiveQuantity
        );

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void UpdateRateDetail(
        Guid rateDetailId,
        Guid? costId,
        string name,
        CostDetailType costDetailType,
        CostType costType,
        ChargeBasis chargeBasis,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount,
        string? notes,
        decimal quantity,
        Guid? updatedBy
    )
    {
        var detail = _rateDetails.FirstOrDefault(x => x.Id == rateDetailId)
            ?? throw new InvalidOperationException("El detalle de la tarifa no existe.");
        ValidateDetail(name, currencyId, currencyName, currencyCode, costAmount, saleAmount);
        var effectiveQuantity = ResolveChargeQuantity(chargeBasis, quantity);
        detail.Update(costId, name.Trim(), costDetailType, costType, chargeBasis, currencyId,
            currencyName.Trim(), currencyCode.Trim(), costAmount, saleAmount, Normalize(notes), effectiveQuantity);
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void RemoveRateDetail(Guid rateDetailId, Guid? updatedBy)
    {
        var detail = _rateDetails.FirstOrDefault(x => x.Id == rateDetailId);
        if (detail is null)
            return;

        _rateDetails.Remove(detail);
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void RemoveAutomaticFixedDetails(Guid? updatedBy)
    {
        var automaticDetails = _rateDetails
            .Where(x => x.CostId.HasValue && x.CostType == CostType.Fixed)
            .ToArray();

        foreach (var detail in automaticDetails)
        {
            _rateDetails.Remove(detail);
        }

        if (automaticDetails.Length > 0)
        {
            MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
        }
    }

    public void SetAmounts(Guid? updatedBy)
    {
        TotalCostAmount = _rateDetails.Sum(x => x.CostAmount * x.Quantity);
        TotalSaleAmount = _rateDetails.Sum(x => x.SaleAmount * x.Quantity);
        TotalUtilityAmount = TotalSaleAmount - TotalCostAmount;
        MarginPercentage =
            TotalSaleAmount <= 0m
                ? 0m
                : Math.Round(
                    TotalUtilityAmount / TotalSaleAmount * 100m,
                    2,
                    MidpointRounding.AwayFromZero
                );

        if (IdtraNumber is not null && QuoNumber is not null)
        {
            RequiredApproval = false;
            Status = RateStatus.AcceptedByClient;
        }
        else if (MarginPercentage >= MinimumMarginPercentage)
        {
            RequiredApproval = false;
            Status = RateStatus.Open;
        }
        else
        {
            RequiredApproval = true;
            Status = RateStatus.PendingApproval;
        }

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
        AddDomainEvent(
            new RateHeaderAmountsChangedDomainEvent(
                Id,
                TotalCostAmount,
                TotalSaleAmount,
                TotalUtilityAmount,
                MarginPercentage,
                updatedBy
            )
        );
    }

    public void SetApprovalMargin(
        Guid? updatedBy,
        bool isApproved,
        bool openAfterAutomaticApproval = false
    )
    {
        if (Status != RateStatus.PendingApproval)
        {
            throw new InvalidOperationException("La tarifa no está pendiente de aprobación.");
        }

        Status = isApproved
            ? openAfterAutomaticApproval
                ? RateStatus.Open
                : RateStatus.ApprovedByManagement
            : RateStatus.RejectedByManagement;
        RequiredApproval = false;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void SetCommercialStatus(RateStatus status, string? reason, Guid? updatedBy)
    {
        var isClosing = status == RateStatus.Closed;
        var isValidTransition = (Status, status) switch
        {
            (RateStatus.ApprovedByManagement, RateStatus.Open) => true,
            // Una solicitud puede guardarse antes de tener proveedor/costos terminados.
            // RequestedByClient funciona como la cola interna de "Abiertas" de Pricing.
            (RateStatus.PendingApproval, RateStatus.RequestedByClient) => true,
            (RateStatus.PendingApproval, RateStatus.Open) => true,
            (RateStatus.Open, RateStatus.Sent) => true,
            (RateStatus.Sent, RateStatus.RequestedByClient) => true,
            (RateStatus.Sent, RateStatus.AcceptedByClient) => true,
            (RateStatus.Sent, RateStatus.RejectedByClient) => true,
            (RateStatus.RequestedByClient, RateStatus.AcceptedByClient) => true,
            (RateStatus.RequestedByClient, RateStatus.RejectedByClient) => true,
            (RateStatus.PendingApproval, RateStatus.Closed) => true,
            (RateStatus.ApprovedByManagement, RateStatus.Closed) => true,
            (RateStatus.RejectedByManagement, RateStatus.Closed) => true,
            (RateStatus.Open, RateStatus.Closed) => true,
            (RateStatus.Sent, RateStatus.Closed) => true,
            (RateStatus.RequestedByClient, RateStatus.Closed) => true,
            _ => false,
        };

        if (!isValidTransition)
        {
            throw new InvalidOperationException(
                "La transición de estado comercial solicitada no es válida."
            );
        }

        if (status == RateStatus.AcceptedByClient && string.IsNullOrWhiteSpace(IdtraNumber))
        {
            throw new InvalidOperationException("Para aceptar la tarifa debe registrar el IDTRA.");
        }

        if (status == RateStatus.RejectedByClient && string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("El motivo de no aceptación del cliente es obligatorio.");
        }

        if (isClosing && string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("El motivo de cierre es obligatorio.");
        }

        if ((isClosing || status == RateStatus.RejectedByClient) && reason!.Trim().Length > 1000)
        {
            throw new InvalidOperationException(
                "El motivo de cierre no puede superar los 1000 caracteres."
            );
        }

        Status = status;
        RequiredApproval = false;

        if (isClosing || status == RateStatus.RejectedByClient)
        {
            ClosedReason = reason?.Trim();
            ClosedAtUtc = DateTime.UtcNow;
            ClosedBy = updatedBy;
        }

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
        AddDomainEvent(new RateHeaderUpdatedDomainEvent(Id, updatedBy));
    }

    public bool MarkExpired(DateTime evaluatedAtUtc, Guid? updatedBy = null)
    {
        var effectiveDate = evaluatedAtUtc.Kind == DateTimeKind.Utc
            ? evaluatedAtUtc.Date
            : evaluatedAtUtc.ToUniversalTime().Date;

        // Comercialmente una tarifa solo puede vencer después de haber sido enviada al cliente.
        // Solicitudes abiertas, pendientes internas y tarifas ya decididas no deben convertirse en Vencidas.
        if (ValidTo.Date >= effectiveDate || Status != RateStatus.Sent)
        {
            return false;
        }

        Status = RateStatus.Expired;
        RequiredApproval = false;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
        AddDomainEvent(new RateHeaderUpdatedDomainEvent(Id, updatedBy));

        return true;
    }

    public void Delete(Guid? deletedBy)
    {
        MarkAsDeleted(DateTime.UtcNow, deletedBy?.ToString());
        AddDomainEvent(new RateHeaderDeletedDomainEvent(Id, deletedBy));
    }

    private static string ValidateRateCode(string rateCode)
    {
        var normalized = rateCode?.Trim().ToUpperInvariant();

        if (
            normalized is null
            || normalized.Length != 16
            || !normalized.StartsWith("QUO-", StringComparison.Ordinal)
            || normalized[9] != '-'
            || !IsAsciiAlphanumeric(normalized.AsSpan(4, 5))
            || !IsAsciiAlphanumeric(normalized.AsSpan(10, 6))
        )
        {
            throw new InvalidOperationException(
                "El identificador QUO debe cumplir el formato alfanumérico QUO-XXXXX-XXXXXX."
            );
        }

        return normalized;
    }

    private static bool IsAsciiAlphanumeric(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            var isDigit = character is >= '0' and <= '9';
            var isUppercaseLetter = character is >= 'A' and <= 'Z';

            if (!isDigit && !isUppercaseLetter)
            {
                return false;
            }
        }

        return true;
    }

    private static string CreateRateName(
        string rateCode,
        string containerDescription,
        string polName,
        string poeName,
        string? podName,
        string? incoterm,
        string? clientName
    )
    {
        var via = poeName switch
        {
            string name when name.Contains("caldera", StringComparison.OrdinalIgnoreCase) =>
                "Caldera",
            string name
                when name.Contains("limon", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("limón", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("moin", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("moín", StringComparison.OrdinalIgnoreCase) => "Limón/Moín",
            string name
                when name.Contains("manzanillo", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("colon", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("colón", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("rodman", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("cristobal", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("cristóbal", StringComparison.OrdinalIgnoreCase) =>
                "Multimodal",
            _ => poeName,
        };

        var incotermLabel = string.IsNullOrWhiteSpace(incoterm) ? "FOB" : incoterm.Trim();
        var hasPod = !string.IsNullOrWhiteSpace(podName);
        var destination = hasPod ? podName!.Trim() : poeName;
        var route = hasPod && !string.Equals(destination, poeName, StringComparison.OrdinalIgnoreCase)
            ? $"{polName} To {destination} Via {via}"
            : $"{polName} To {destination}";
        var baseName =
            $"{rateCode} - Tarifa {containerDescription} - {incotermLabel} - {route}";
        return string.IsNullOrWhiteSpace(clientName) ? baseName : $"{baseName} - {clientName}";
    }

    private string BuildShipmentDescription()
    {
        return ShipmentMode switch
        {
            ShipmentMode.Lcl => $"LCL · {ChargeableQuantity:0.###} CBM cobrable",
            ShipmentMode.Ltl => $"LTL · {ChargeableQuantity:0.###} CBM cobrable",
            ShipmentMode.Ftl => $"{Math.Max(ContainerQuantity, 1)} x FTL",
            _ => BuildContainerDescription(),
        };
    }

    private string BuildContainerDescription()
    {
        if (_rateContainers.Count == 0)
        {
            return $"{ContainerQuantity} x {ContainerTypeName}";
        }

        return string.Join(
            " + ",
            _rateContainers
                .OrderBy(x => x.ContainerTypeName)
                .ThenBy(x => x.ContainerTypeCode)
                .Select(x => $"{x.Quantity} x {x.ContainerTypeName}")
        );
    }

    private static void ValidateHeader(
        Guid? agentId,
        string? agentName,
        string? agentCode,
        Guid? carrierId,
        string? carrierName,
        string? carrierCode,
        Guid polId,
        string polName,
        string polCode,
        Guid poeId,
        string poeName,
        string poeCode,
        Guid? podId,
        string? podName,
        string? podCode,
        Guid containerTypeId,
        string containerTypeName,
        string containerTypeCode,
        Guid? incotermId,
        string? incotermName,
        string? incotermCode,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        int freeDays,
        DateTime validFrom,
        DateTime validTo,
        int containerQuantity
    )
    {
        if (
            !agentId.HasValue
            || agentId.Value == Guid.Empty
            || string.IsNullOrWhiteSpace(agentName)
            || string.IsNullOrWhiteSpace(agentCode)
        )
        {
            throw new InvalidOperationException("El agente es obligatorio.");
        }

        if (
            !carrierId.HasValue
            || carrierId.Value == Guid.Empty
            || string.IsNullOrWhiteSpace(carrierName)
            || string.IsNullOrWhiteSpace(carrierCode)
        )
        {
            throw new InvalidOperationException("La naviera es obligatoria.");
        }

        var normalizedIncotermId = NormalizeId(incotermId);
        if (
            normalizedIncotermId.HasValue
            && (string.IsNullOrWhiteSpace(incotermName) || string.IsNullOrWhiteSpace(incotermCode))
        )
        {
            throw new InvalidOperationException("El Incoterm seleccionado debe incluir nombre y código.");
        }

        if (
            !normalizedIncotermId.HasValue
            && (!string.IsNullOrWhiteSpace(incotermName) || !string.IsNullOrWhiteSpace(incotermCode))
        )
        {
            throw new InvalidOperationException("El identificador del Incoterm es obligatorio cuando se envía su información.");
        }

        if (
            polId == Guid.Empty
            || string.IsNullOrWhiteSpace(polName)
            || string.IsNullOrWhiteSpace(polCode)
        )
        {
            throw new InvalidOperationException("El POL es obligatorio.");
        }

        if (
            poeId == Guid.Empty
            || string.IsNullOrWhiteSpace(poeName)
            || string.IsNullOrWhiteSpace(poeCode)
        )
        {
            throw new InvalidOperationException("El POE es obligatorio.");
        }

        var normalizedPodId = NormalizeId(podId);
        if (
            normalizedPodId.HasValue
            && (string.IsNullOrWhiteSpace(podName) || string.IsNullOrWhiteSpace(podCode))
        )
        {
            throw new InvalidOperationException("El POD seleccionado debe incluir nombre y código.");
        }

        if (
            !normalizedPodId.HasValue
            && (!string.IsNullOrWhiteSpace(podName) || !string.IsNullOrWhiteSpace(podCode))
        )
        {
            throw new InvalidOperationException("El identificador del POD es obligatorio cuando se envía su información.");
        }

        if (
            containerTypeId == Guid.Empty
            || string.IsNullOrWhiteSpace(containerTypeName)
            || string.IsNullOrWhiteSpace(containerTypeCode)
        )
        {
            throw new InvalidOperationException("El tipo de contenedor es obligatorio.");
        }

        if (
            currencyId == Guid.Empty
            || string.IsNullOrWhiteSpace(currencyName)
            || string.IsNullOrWhiteSpace(currencyCode)
        )
        {
            throw new InvalidOperationException("La moneda es obligatoria.");
        }

        if (freeDays < 0)
        {
            throw new InvalidOperationException("Los días libres no pueden ser negativos.");
        }

        if (containerQuantity <= 0)
        {
            throw new InvalidOperationException(
                "La cantidad de contenedores debe ser mayor que cero."
            );
        }

        if (validTo.Date < validFrom.Date)
        {
            throw new InvalidOperationException(
                "La fecha final no puede ser menor a la fecha inicial."
            );
        }
    }

    private void SynchronizeFreightQuantities()
    {
        foreach (var detail in _rateDetails)
        {
            var isMetricBased = detail.ChargeBasis is
                ChargeBasis.PerCbm or
                ChargeBasis.PerChargeableCbm or
                ChargeBasis.PerKg or
                ChargeBasis.Per100Kg or
                ChargeBasis.PerTon or
                ChargeBasis.PerPallet or
                ChargeBasis.PerPackage;

            if (isMetricBased)
            {
                detail.SetQuantity(ResolveChargeQuantity(detail.ChargeBasis));
                continue;
            }

            // Keep explicit ocean-freight quantities per equipment type. Inland transport and
            // automatic fixed equipment costs still follow the shipment's equipment quantity.
            var followsAllEquipment =
                detail.CostDetailType == CostDetailType.InlandTransport
                || (detail.CostId.HasValue && detail.CostType == CostType.Fixed);

            if (followsAllEquipment && detail.ChargeBasis is ChargeBasis.PerContainer or ChargeBasis.PerTruck)
                detail.SetQuantity(ResolveChargeQuantity(detail.ChargeBasis));
        }
    }

    private static void ValidateDetail(
        string name,
        Guid currencyId,
        string currencyName,
        string currencyCode,
        decimal costAmount,
        decimal saleAmount
    )
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("El nombre del detalle es obligatorio.");
        }

        if (
            currencyId == Guid.Empty
            || string.IsNullOrWhiteSpace(currencyName)
            || string.IsNullOrWhiteSpace(currencyCode)
        )
        {
            throw new InvalidOperationException("La moneda del detalle es obligatoria.");
        }

        if (costAmount < 0m || saleAmount < 0m)
        {
            throw new InvalidOperationException("Los montos del detalle no pueden ser negativos.");
        }
    }


    private static void ValidateRateTerms(string? includes, string? subjectTo, string? excludes)
    {
        static string TermKey(string value)
        {
            var normalized = System.Text.RegularExpressions.Regex.Replace(
                value.Trim().ToUpperInvariant(),
                @"[^\p{L}\p{N}]+",
                " "
            ).Trim();
            var qualifier = System.Text.RegularExpressions.Regex.Match(
                normalized,
                @"\s(?:USD|EUR|CRC|IVI|IVA|ITBMS|\d)"
            );
            return qualifier.Success && qualifier.Index > 0
                ? normalized[..qualifier.Index].Trim()
                : normalized;
        }

        static HashSet<string> Parse(string? value) => string.IsNullOrWhiteSpace(value)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : value
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(TermKey)
                .Where(x => x.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var included = Parse(includes);
        var subject = Parse(subjectTo);
        var excluded = Parse(excludes);

        var duplicate = included.FirstOrDefault(subject.Contains)
            ?? included.FirstOrDefault(excluded.Contains)
            ?? subject.FirstOrDefault(excluded.Contains);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"El ítem '{duplicate}' solo puede pertenecer a una categoría: Incluye, Sujeto a o No incluye."
            );
        }
    }

    private static Guid? NormalizeId(Guid? value) =>
        value.HasValue && value.Value != Guid.Empty ? value : null;

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
