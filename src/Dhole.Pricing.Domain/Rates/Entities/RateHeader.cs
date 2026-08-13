using CustomCodeFramework.Core.Domain.Entities;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Rates.Events;

namespace Dhole.Pricing.Domain.Rates.Entities;

public sealed class RateHeader : SoftDeletableAggregateRoot<Guid>
{
    private const decimal MinimumMarginPercentage = 12m;
    private readonly List<RateDetail> _rateDetails = [];

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
        Guid podId,
        string podName,
        string podCode,
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
        int? transitDays,
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
            containerQuantity,
            transitDays
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
        PodId = podId;
        PodName = podName.Trim();
        PodCode = podCode.Trim();
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
        ClientName = Normalize(clientName);
        IdtraNumber = Normalize(idtraNumber);
        QuoNumber = Normalize(quoNumber);
        Includes = Normalize(includes);
        SubjectTo = Normalize(subjectTo);
        Excludes = Normalize(excludes);
        TransitDays = transitDays;
        RateCode = ValidateRateCode(rateCode);
        RateName = CreateRateName(
            RateCode,
            ContainerQuantity,
            ContainerTypeName,
            PolName,
            PoeName,
            PodName,
            IncotermCode ?? IncotermName,
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

    public Guid PodId { get; private set; }
    public string PodName { get; private set; } = string.Empty;
    public string PodCode { get; private set; } = string.Empty;

    public Guid ContainerTypeId { get; private set; }
    public string ContainerTypeName { get; private set; } = string.Empty;
    public string ContainerTypeCode { get; private set; } = string.Empty;

    public Guid? IncotermId { get; private set; }
    public string? IncotermName { get; private set; }
    public string? IncotermCode { get; private set; }

    public Guid CurrencyId { get; private set; }
    public string CurrencyName { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;

    public int FreeDays { get; private set; }
    public DateTime ValidFrom { get; private set; }
    public DateTime ValidTo { get; private set; }

    public string RateCode { get; private set; } = string.Empty;
    public string RateName { get; private set; } = string.Empty;
    public int ContainerQuantity { get; private set; }

    public string? ClientName { get; private set; }
    public string? IdtraNumber { get; private set; }
    public string? QuoNumber { get; private set; }
    public string? Includes { get; private set; }
    public string? SubjectTo { get; private set; }
    public string? Excludes { get; private set; }
    public int? TransitDays { get; private set; }

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
        Guid podId,
        string podName,
        string podCode,
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
        int? transitDays,
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
            transitDays,
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
        Guid podId,
        string podName,
        string podCode,
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
        int? transitDays,
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
            containerQuantity,
            transitDays
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
        PodId = podId;
        PodName = podName.Trim();
        PodCode = podCode.Trim();
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
        TransitDays = transitDays;

        RateName = CreateRateName(
            RateCode,
            ContainerQuantity,
            ContainerTypeName,
            PolName,
            PoeName,
            PodName,
            IncotermCode ?? IncotermName,
            ClientName
        );

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
        AddDomainEvent(new RateHeaderUpdatedDomainEvent(Id, updatedBy));
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

        var effectiveQuantity = ResolveDetailQuantity(costDetailType, quantity);

        var detail = RateDetail.Create(
            Id,
            costId,
            name.Trim(),
            costDetailType,
            costType,
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

        var effectiveQuantity = ResolveDetailQuantity(costDetailType, quantity);

        detail.Update(
            costId,
            name.Trim(),
            costDetailType,
            costType,
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

        if (isClosing && string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("El motivo de cierre es obligatorio.");
        }

        if (isClosing && reason!.Trim().Length > 1000)
        {
            throw new InvalidOperationException(
                "El motivo de cierre no puede superar los 1000 caracteres."
            );
        }

        Status = status;
        RequiredApproval = false;

        if (isClosing)
        {
            ClosedReason = reason!.Trim();
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

        if (ValidTo.Date >= effectiveDate || Status == RateStatus.Expired)
        {
            return false;
        }

        if (
            Status
            is RateStatus.Closed
                or RateStatus.RejectedByManagement
                or RateStatus.RejectedByClient
        )
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
        int containerQuantity,
        string containerTypeName,
        string polName,
        string poeName,
        string podName,
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
        var baseName =
            $"{rateCode} - Tarifa {containerQuantity} x {containerTypeName} - {incotermLabel} - {polName} To {podName} Via {via}";
        return string.IsNullOrWhiteSpace(clientName) ? baseName : $"{baseName} - {clientName}";
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
        Guid podId,
        string podName,
        string podCode,
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
        int? transitDays
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

        if (
            podId == Guid.Empty
            || string.IsNullOrWhiteSpace(podName)
            || string.IsNullOrWhiteSpace(podCode)
        )
        {
            throw new InvalidOperationException("El POD es obligatorio.");
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

        if (freeDays < 0 || transitDays is < 0)
        {
            throw new InvalidOperationException(
                "Los días libres y el tiempo de tránsito no pueden ser negativos."
            );
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
        foreach (var detail in _rateDetails.Where(x => IsFreightPerContainer(x.CostDetailType)))
        {
            detail.SetQuantity(ContainerQuantity);
        }
    }

    private int ResolveDetailQuantity(CostDetailType costDetailType, int requestedQuantity)
    {
        if (requestedQuantity <= 0)
        {
            throw new InvalidOperationException("La cantidad del detalle debe ser mayor que cero.");
        }

        return IsFreightPerContainer(costDetailType) ? ContainerQuantity : requestedQuantity;
    }

    private static bool IsFreightPerContainer(CostDetailType costDetailType) =>
        costDetailType is CostDetailType.Freight or CostDetailType.InlandTransport;

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
        static HashSet<string> Parse(string? value) => string.IsNullOrWhiteSpace(value)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : value
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
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
