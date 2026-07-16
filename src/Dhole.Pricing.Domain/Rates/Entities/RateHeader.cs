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
        long rateConsecutive,
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
        Guid currencyId,
        string currencyName,
        string currencyCode,
        int freeDays,
        DateTime validFrom,
        DateTime validTo,
        int containerQuantity,
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
            currencyId,
            currencyName,
            currencyCode,
            freeDays,
            validFrom,
            validTo
        );

        SourceImportFclRateId = sourceImportFclRateId;

        AgentId = agentId;
        AgentName = agentName!.Trim();
        AgentCode = agentCode!.Trim();

        CarrierId = carrierId;
        CarrierName = carrierName!.Trim();
        CarrierCode = carrierCode!.Trim();

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

        CurrencyId = currencyId;
        CurrencyName = currencyName.Trim();
        CurrencyCode = currencyCode.Trim();

        FreeDays = freeDays;
        ValidFrom = validFrom;
        ValidTo = validTo;

        ContainerQuantity = containerQuantity;

        RateCode = CreateRateCode(rateConsecutive);

        RateName = CreateRateName(
            RateCode,
            ContainerQuantity,
            ContainerTypeName,
            PolName,
            PoeName,
            PodName
        );

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

    public decimal TotalCostAmount { get; private set; }
    public decimal TotalSaleAmount { get; private set; }
    public decimal TotalUtilityAmount { get; private set; }
    public decimal MarginPercentage { get; private set; }

    public bool RequiredApproval { get; private set; }

    public RateStatus Status { get; private set; }

    public IReadOnlyCollection<RateDetail> RateDetails => _rateDetails.AsReadOnly();

    public static RateHeader Create(
        long rateConsecutive,
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
        Guid currencyId,
        string currencyName,
        string currencyCode,
        int freeDays,
        DateTime validFrom,
        DateTime validTo,
        int containerQuantity,
        Guid? createdBy
    )
    {
        var rate = new RateHeader(
            Guid.NewGuid(),
            rateConsecutive,
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
            currencyId,
            currencyName,
            currencyCode,
            freeDays,
            validFrom,
            validTo,
            containerQuantity,
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
        Guid currencyId,
        string currencyName,
        string currencyCode,
        int freeDays,
        DateTime validFrom,
        DateTime validTo,
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
            currencyId,
            currencyName,
            currencyCode,
            freeDays,
            validFrom,
            validTo
        );

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

        CurrencyId = currencyId;
        CurrencyName = currencyName.Trim();
        CurrencyCode = currencyCode.Trim();

        FreeDays = freeDays;
        ValidFrom = validFrom;
        ValidTo = validTo;

        RateName = CreateRateName(
            RateCode,
            ContainerQuantity,
            ContainerTypeName,
            PolName,
            PoeName,
            PodName
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
            quantity
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
            quantity
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

        if (automaticDetails.Length == 0)
            return;

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void SetAmounts(Guid? updatedBy)
    {
        TotalCostAmount = _rateDetails.Sum(x => x.CostAmount);

        TotalSaleAmount = _rateDetails.Sum(x => x.SaleAmount);

        TotalUtilityAmount = TotalSaleAmount - TotalCostAmount;

        MarginPercentage =
            TotalSaleAmount <= 0m
                ? 0m
                : Math.Round(
                    TotalUtilityAmount / TotalSaleAmount * 100m,
                    2,
                    MidpointRounding.AwayFromZero
                );

        if (_rateDetails.Count == 0 || TotalSaleAmount <= 0m)
        {
            RequiredApproval = false;
            Status = RateStatus.Draft;
        }
        else if (MarginPercentage >= MinimumMarginPercentage)
        {
            RequiredApproval = false;
            Status = RateStatus.Approved;
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

    public void SetApprovalMargin(Guid? updatedBy, bool isApproved)
    {
        if (Status != RateStatus.PendingApproval)
        {
            throw new InvalidOperationException("La tarifa no está pendiente de aprobación.");
        }

        Status = isApproved ? RateStatus.Approved : RateStatus.Rejected;

        RequiredApproval = false;

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void Delete(Guid? deletedBy)
    {
        MarkAsDeleted(DateTime.UtcNow, deletedBy?.ToString());

        AddDomainEvent(new RateHeaderDeletedDomainEvent(Id, deletedBy));
    }

    private static string CreateRateCode(long consecutive)
    {
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const long maximumConsecutive = 2_176_782_335L; // 36^6 - 1

        if (consecutive is < 1 or > maximumConsecutive)
        {
            throw new InvalidOperationException(
                "El consecutivo de la tarifa está fuera del rango permitido."
            );
        }

        Span<char> code = stackalloc char[6];
        var value = consecutive;

        for (var index = code.Length - 1; index >= 0; index--)
        {
            code[index] = alphabet[(int)(value % alphabet.Length)];
            value /= alphabet.Length;
        }

        return $"QUO-{new string(code)}";
    }

    private static string CreateRateName(
        string rateCode,
        int containerQuantity,
        string containerTypeName,
        string polName,
        string poeName,
        string podName
    )
    {
        var via = poeName switch
        {
            string name when name.Contains("caldera", StringComparison.OrdinalIgnoreCase) =>
                "Caldera",

            string name
                when name.Contains("limon", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("moín", StringComparison.OrdinalIgnoreCase) => "Limón/Moín",

            string name
                when name.Contains("manzanillo", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("colon", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("rodman", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("cristobal", StringComparison.OrdinalIgnoreCase) =>
                "Multimodal",

            _ => "Desconocida",
        };

        return $"{rateCode} - Tarifa {containerQuantity} x {containerTypeName} - FOB - {polName} To {podName} Via {via}";
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
        Guid currencyId,
        string currencyName,
        string currencyCode,
        int freeDays,
        DateTime validFrom,
        DateTime validTo
    )
    {
        if (
            agentId == Guid.Empty
            || string.IsNullOrWhiteSpace(agentName)
            || string.IsNullOrWhiteSpace(agentCode)
        )
        {
            throw new InvalidOperationException("El agente es obligatorio.");
        }

        if (
            carrierId == Guid.Empty
            || string.IsNullOrWhiteSpace(carrierName)
            || string.IsNullOrWhiteSpace(carrierCode)
        )
        {
            throw new InvalidOperationException("La naviera es obligatoria.");
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

        if (freeDays < 0)
        {
            throw new InvalidOperationException("Los días libres no pueden ser negativos.");
        }

        if (validTo.Date < validFrom.Date)
        {
            throw new InvalidOperationException(
                "La fecha final no puede ser menor a la fecha inicial."
            );
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

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
