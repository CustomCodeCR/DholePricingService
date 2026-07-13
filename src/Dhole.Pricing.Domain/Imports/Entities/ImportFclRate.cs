using CustomCodeFramework.Core.Domain.Entities;
using Dhole.Pricing.Domain.Imports.Enums;
using Dhole.Pricing.Domain.Imports.Events;

namespace Dhole.Pricing.Domain.Imports.Entities;

public sealed class ImportFclRates : SoftDeletableAggregateRoot<Guid>
{
    private ImportFclRates() { }

    private ImportFclRates(
        Guid id,
        Guid importBatchId,
        Guid extractionRecordId,
        ImportSourceType sourceType,
        CatalogSnapshot profile,
        CatalogSnapshot pol,
        CatalogSnapshot poe,
        CatalogSnapshot pod,
        CatalogSnapshot carrier,
        CatalogSnapshot agent,
        CatalogSnapshot containerType,
        CatalogSnapshot currency,
        string? commodity,
        decimal? oceanFreight,
        decimal? originCharges,
        decimal? destinationCharges,
        decimal? surcharges,
        decimal? totalCost,
        decimal? totalSale,
        decimal? profit,
        decimal? margin,
        int freeDays,
        int? transitDays,
        DateTime validFrom,
        DateTime validTo,
        string? rawDataJson,
        Guid? createdBy
    )
        : base(id)
    {
        EnsureValidDates(validFrom, validTo);
        EnsureNonNegative(oceanFreight, nameof(oceanFreight));
        EnsureNonNegative(originCharges, nameof(originCharges));
        EnsureNonNegative(destinationCharges, nameof(destinationCharges));
        EnsureNonNegative(surcharges, nameof(surcharges));

        if (freeDays < 0 || transitDays < 0)
        {
            throw new InvalidOperationException(
                "Los días libres y los días de tránsito no pueden ser negativos."
            );
        }

        ImportBatchId = importBatchId;
        ExtractionRecordId = extractionRecordId;
        SourceType = sourceType;

        ApplyProfile(profile);
        ApplyPol(pol);
        ApplyPoe(poe);
        ApplyPod(pod);
        ApplyCarrier(carrier);
        ApplyAgent(agent);
        ApplyContainerType(containerType);
        ApplyCurrency(currency);

        Commodity = Normalize(commodity);
        OceanFreight = oceanFreight;
        OriginCharges = originCharges;
        DestinationCharges = destinationCharges;
        Surcharges = surcharges;
        TotalCost = totalCost;
        TotalSale = totalSale;
        Profit = profit;
        Margin = margin;

        // Compatibilidad con las consultas existentes que utilizan Freight.
        Freight = oceanFreight ?? 0m;
        FreeDays = freeDays;
        TransitDays = transitDays;
        ValidFrom = validFrom;
        ValidTo = validTo;
        RawDataJson = Normalize(rawDataJson);
        Status = ImportStatus.Pending;

        MarkAsCreated(DateTime.UtcNow, createdBy?.ToString());
    }

    public Guid ImportBatchId { get; private set; }
    public Guid ExtractionRecordId { get; private set; }
    public ImportSourceType SourceType { get; private set; }

    public Guid ImportProfileId { get; private set; }
    public string ImportProfileName { get; private set; } = string.Empty;
    public string ImportProfileCode { get; private set; } = string.Empty;
    public string ImportProfileSlug { get; private set; } = string.Empty;

    public Guid PolId { get; private set; }
    public string Pol { get; private set; } = string.Empty;
    public string PolName { get; private set; } = string.Empty;
    public string PolCode { get; private set; } = string.Empty;
    public string PolSlug { get; private set; } = string.Empty;

    public Guid PoeId { get; private set; }
    public string Poe { get; private set; } = string.Empty;
    public string PoeName { get; private set; } = string.Empty;
    public string PoeCode { get; private set; } = string.Empty;
    public string PoeSlug { get; private set; } = string.Empty;

    public Guid PodId { get; private set; }
    public string Pod { get; private set; } = string.Empty;
    public string PodName { get; private set; } = string.Empty;
    public string PodCode { get; private set; } = string.Empty;
    public string PodSlug { get; private set; } = string.Empty;

    public Guid CarrierId { get; private set; }
    public string Carrier { get; private set; } = string.Empty;
    public string CarrierName { get; private set; } = string.Empty;
    public string CarrierCode { get; private set; } = string.Empty;
    public string CarrierSlug { get; private set; } = string.Empty;

    public Guid AgentId { get; private set; }
    public string Agent { get; private set; } = string.Empty;
    public string AgentName { get; private set; } = string.Empty;
    public string AgentCode { get; private set; } = string.Empty;
    public string AgentSlug { get; private set; } = string.Empty;

    public Guid ContainerTypeId { get; private set; }
    public string ContainerType { get; private set; } = string.Empty;
    public string ContainerTypeName { get; private set; } = string.Empty;
    public string ContainerTypeCode { get; private set; } = string.Empty;
    public string ContainerTypeSlug { get; private set; } = string.Empty;

    public Guid CurrencyId { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string CurrencyName { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;
    public string CurrencySlug { get; private set; } = string.Empty;

    public string? Commodity { get; private set; }
    public decimal Freight { get; private set; }
    public decimal? OceanFreight { get; private set; }
    public decimal? OriginCharges { get; private set; }
    public decimal? DestinationCharges { get; private set; }
    public decimal? Surcharges { get; private set; }
    public decimal? TotalCost { get; private set; }
    public decimal? TotalSale { get; private set; }
    public decimal? Profit { get; private set; }
    public decimal? Margin { get; private set; }

    public int FreeDays { get; private set; }
    public int? TransitDays { get; private set; }
    public DateTime ValidFrom { get; private set; }
    public DateTime ValidTo { get; private set; }
    public ImportStatus Status { get; private set; }
    public string? RawDataJson { get; private set; }
    public string? SourceUrl { get; private set; }
    public int UsedAsRateCount { get; private set; }
    public Guid? CreatedAsRateHeaderId { get; private set; }

    public static ImportFclRates Create(
        Guid importBatchId,
        Guid extractionRecordId,
        ImportSourceType sourceType,
        CatalogSnapshot profile,
        CatalogSnapshot pol,
        CatalogSnapshot poe,
        CatalogSnapshot pod,
        CatalogSnapshot carrier,
        CatalogSnapshot agent,
        CatalogSnapshot containerType,
        CatalogSnapshot currency,
        string? commodity,
        decimal? oceanFreight,
        decimal? originCharges,
        decimal? destinationCharges,
        decimal? surcharges,
        decimal? totalCost,
        decimal? totalSale,
        decimal? profit,
        decimal? margin,
        int freeDays,
        int? transitDays,
        DateTime validFrom,
        DateTime validTo,
        string? rawDataJson,
        Guid? createdBy
    )
    {
        var rate = new ImportFclRates(
            Guid.NewGuid(),
            importBatchId,
            extractionRecordId,
            sourceType,
            profile,
            pol,
            poe,
            pod,
            carrier,
            agent,
            containerType,
            currency,
            commodity,
            oceanFreight,
            originCharges,
            destinationCharges,
            surcharges,
            totalCost,
            totalSale,
            profit,
            margin,
            freeDays,
            transitDays,
            validFrom,
            validTo,
            rawDataJson,
            createdBy
        );

        return rate;
    }

    public void Approve(Guid? updatedBy = null)
    {
        Status = ImportStatus.Approved;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
        AddDomainEvent(new ImportFclRateApprovedDomainEvent(Id, Status, updatedBy));
    }

    public void Reject(Guid? updatedBy)
    {
        Status = ImportStatus.Rejected;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
        AddDomainEvent(new ImportFclRateRejectDomainEvent(Id, Status, updatedBy));
    }

    public void CreatedAsRate(Guid rateHeaderId, Guid? updatedBy = null)
    {
        if (rateHeaderId == Guid.Empty)
            throw new InvalidOperationException("La tarifa oficial creada es requerida.");

        Status = ImportStatus.Approved;
        CreatedAsRateHeaderId = rateHeaderId;
        UsedAsRateCount += 1;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
        AddDomainEvent(
            new ImportFclRateCreatedAsRateDomainEvent(Id, rateHeaderId, Status, updatedBy)
        );
    }

    public void Delete(Guid? deletedBy = null)
    {
        MarkAsDeleted(DateTime.UtcNow, deletedBy?.ToString());
        AddDomainEvent(new ImportFclRateDeletedDomainEvent(Id, deletedBy));
    }

    private void ApplyProfile(CatalogSnapshot value)
    {
        ImportProfileId = value.Id;
        ImportProfileName = value.Name;
        ImportProfileCode = value.Code;
        ImportProfileSlug = value.Slug;
    }

    private void ApplyPol(CatalogSnapshot value)
    {
        PolId = value.Id;
        Pol = value.Code;
        PolName = value.Name;
        PolCode = value.Code;
        PolSlug = value.Slug;
    }

    private void ApplyPoe(CatalogSnapshot value)
    {
        PoeId = value.Id;
        Poe = value.Code;
        PoeName = value.Name;
        PoeCode = value.Code;
        PoeSlug = value.Slug;
    }

    private void ApplyPod(CatalogSnapshot value)
    {
        PodId = value.Id;
        Pod = value.Code;
        PodName = value.Name;
        PodCode = value.Code;
        PodSlug = value.Slug;
    }

    private void ApplyCarrier(CatalogSnapshot value)
    {
        CarrierId = value.Id;
        Carrier = value.Code;
        CarrierName = value.Name;
        CarrierCode = value.Code;
        CarrierSlug = value.Slug;
    }

    private void ApplyAgent(CatalogSnapshot value)
    {
        AgentId = value.Id;
        Agent = value.Code;
        AgentName = value.Name;
        AgentCode = value.Code;
        AgentSlug = value.Slug;
    }

    private void ApplyContainerType(CatalogSnapshot value)
    {
        ContainerTypeId = value.Id;
        ContainerType = value.Code;
        ContainerTypeName = value.Name;
        ContainerTypeCode = value.Code;
        ContainerTypeSlug = value.Slug;
    }

    private void ApplyCurrency(CatalogSnapshot value)
    {
        CurrencyId = value.Id;
        Currency = value.Code;
        CurrencyName = value.Name;
        CurrencyCode = value.Code;
        CurrencySlug = value.Slug;
    }

    private static void EnsureValidDates(DateTime validFrom, DateTime validTo)
    {
        if (validTo < validFrom)
            throw new InvalidOperationException(
                "La fecha final de vigencia no puede ser menor a la fecha inicial."
            );
    }

    private static void EnsureNonNegative(decimal? value, string field)
    {
        if (value < 0)
            throw new InvalidOperationException($"El valor de {field} no puede ser negativo.");
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed record CatalogSnapshot(Guid Id, string Name, string Code, string Slug)
{
    public static CatalogSnapshot Create(Guid id, string name, string code, string slug)
    {
        if (
            id == Guid.Empty
            || string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(code)
            || string.IsNullOrWhiteSpace(slug)
        )
        {
            throw new InvalidOperationException(
                "La referencia de catálogo debe contener id, nombre, código y slug."
            );
        }

        return new CatalogSnapshot(id, name.Trim(), code.Trim(), slug.Trim().ToLowerInvariant());
    }
}
