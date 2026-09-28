using CustomCodeFramework.Core.Domain.Entities;
using Dhole.Pricing.Domain.MarketPricing.Enums;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Domain.MarketPricing.Entities;

public sealed class CompetitorRateObservation : Entity<Guid>
{
    private CompetitorRateObservation() { }

    private CompetitorRateObservation(
        Guid id,
        Guid? competitorTariffId,
        Guid? competitorCompanyId,
        string competitorCompanyName,
        Guid? sourceDocumentId,
        Guid? sourceImportId,
        ShipmentMode mode,
        string currency,
        DateTime validFrom,
        DateTime validTo,
        MarketRateBasis rateBasis,
        decimal? oceanFreight,
        decimal? originCharges,
        decimal? destinationCharges,
        decimal? inlandCharges,
        decimal? otherCharges,
        decimal? originalAmount,
        decimal extractionConfidence,
        string rawPayloadJson,
        DateTime importedAtUtc
    ) : base(id)
    {
        CompetitorTariffId = competitorTariffId;
        CompetitorCompanyId = competitorCompanyId;
        CompetitorCompanyName = RequireText(competitorCompanyName, "La empresa competidora es obligatoria.");
        SourceDocumentId = sourceDocumentId;
        SourceImportId = sourceImportId;

        if (!Enum.IsDefined(mode))
        {
            throw new InvalidOperationException("La modalidad de la observación de mercado no es válida.");
        }

        Mode = mode;
        Currency = RequireText(currency, "La moneda de la observación de mercado es obligatoria.").ToUpperInvariant();

        ValidFrom = NormalizeUtc(validFrom);
        ValidTo = NormalizeUtc(validTo);
        if (ValidTo < ValidFrom)
        {
            throw new InvalidOperationException("La vigencia hasta no puede ser anterior a la vigencia desde.");
        }

        if (!Enum.IsDefined(rateBasis))
        {
            throw new InvalidOperationException("La base de tarifa de mercado no es válida.");
        }

        RateBasis = rateBasis;
        OceanFreight = EnsureNonNegative(oceanFreight, nameof(oceanFreight));
        OriginCharges = EnsureNonNegative(originCharges, nameof(originCharges));
        DestinationCharges = EnsureNonNegative(destinationCharges, nameof(destinationCharges));
        InlandCharges = EnsureNonNegative(inlandCharges, nameof(inlandCharges));
        OtherCharges = EnsureNonNegative(otherCharges, nameof(otherCharges));
        OriginalAmount = EnsureNonNegative(originalAmount, nameof(originalAmount));
        ExtractionConfidence = EnsureConfidence(extractionConfidence, nameof(extractionConfidence));
        RawPayloadJson = RequireText(rawPayloadJson, "El payload original de la observación es obligatorio.");
        ImportedAtUtc = NormalizeUtc(importedAtUtc);
        CreatedAtUtc = DateTime.UtcNow;
        Quantity = 1;
    }

    public Guid? CompetitorTariffId { get; private set; }
    public Guid? CompetitorCompanyId { get; private set; }
    public string CompetitorCompanyName { get; private set; } = string.Empty;

    public Guid? SourceDocumentId { get; private set; }
    public Guid? SourceImportId { get; private set; }

    public Guid? IncotermId { get; private set; }
    public string? IncotermCode { get; private set; }

    public Guid? PolId { get; private set; }
    public string? PolName { get; private set; }
    public string? PolCode { get; private set; }

    public Guid? PoeId { get; private set; }
    public string? PoeName { get; private set; }
    public string? PoeCode { get; private set; }

    public Guid? PodId { get; private set; }
    public string? PodName { get; private set; }
    public string? PodCode { get; private set; }

    public Guid? CarrierId { get; private set; }
    public string? CarrierName { get; private set; }
    public string? CarrierCode { get; private set; }

    public Guid? ContainerTypeId { get; private set; }
    public string? ContainerTypeCode { get; private set; }
    public int Quantity { get; private set; }

    public ShipmentMode Mode { get; private set; }

    public string Currency { get; private set; } = string.Empty;
    public string? NormalizedCurrency { get; private set; }
    public decimal? OriginalAmount { get; private set; }
    public decimal? NormalizedAmount { get; private set; }
    public decimal? ExchangeRate { get; private set; }
    public DateTime? ExchangeRateDate { get; private set; }

    public DateTime ValidFrom { get; private set; }
    public DateTime ValidTo { get; private set; }

    public decimal? OceanFreight { get; private set; }
    public decimal? OriginCharges { get; private set; }
    public decimal? DestinationCharges { get; private set; }
    public decimal? InlandCharges { get; private set; }
    public decimal? OtherCharges { get; private set; }

    public decimal? NormalizedOceanFreight { get; private set; }
    public decimal? NormalizedAllIn { get; private set; }

    public MarketRateBasis RateBasis { get; private set; }

    public decimal ExtractionConfidence { get; private set; }
    public decimal NormalizationConfidence { get; private set; }

    public string RawPayloadJson { get; private set; } = "{}";

    public DateTime ImportedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static CompetitorRateObservation Create(
        Guid? competitorTariffId,
        Guid? competitorCompanyId,
        string competitorCompanyName,
        Guid? sourceDocumentId,
        Guid? sourceImportId,
        ShipmentMode mode,
        string currency,
        DateTime validFrom,
        DateTime validTo,
        MarketRateBasis rateBasis,
        decimal? oceanFreight,
        decimal? originCharges,
        decimal? destinationCharges,
        decimal? inlandCharges,
        decimal? otherCharges,
        decimal? originalAmount,
        decimal extractionConfidence,
        string rawPayloadJson,
        DateTime? importedAtUtc = null
    ) => new(
        Guid.NewGuid(),
        competitorTariffId,
        competitorCompanyId,
        competitorCompanyName,
        sourceDocumentId,
        sourceImportId,
        mode,
        currency,
        validFrom,
        validTo,
        rateBasis,
        oceanFreight,
        originCharges,
        destinationCharges,
        inlandCharges,
        otherCharges,
        originalAmount,
        extractionConfidence,
        rawPayloadJson,
        importedAtUtc ?? DateTime.UtcNow
    );

    public void ApplyNormalization(
        Guid? incotermId,
        string? incotermCode,
        Guid? polId,
        string? polName,
        string? polCode,
        Guid? poeId,
        string? poeName,
        string? poeCode,
        Guid? podId,
        string? podName,
        string? podCode,
        Guid? carrierId,
        string? carrierName,
        string? carrierCode,
        Guid? containerTypeId,
        string? containerTypeCode,
        int quantity,
        decimal? normalizedOceanFreight,
        decimal? normalizedAllIn,
        string? normalizedCurrency,
        decimal? normalizedAmount,
        decimal? exchangeRate,
        DateTime? exchangeRateDate,
        decimal normalizationConfidence
    )
    {
        if (quantity <= 0)
        {
            throw new InvalidOperationException("La cantidad normalizada debe ser mayor que cero.");
        }

        IncotermId = incotermId;
        IncotermCode = NormalizeText(incotermCode);

        PolId = polId;
        PolName = NormalizeText(polName);
        PolCode = NormalizeText(polCode);

        PoeId = poeId;
        PoeName = NormalizeText(poeName);
        PoeCode = NormalizeText(poeCode);

        PodId = podId;
        PodName = NormalizeText(podName);
        PodCode = NormalizeText(podCode);

        CarrierId = carrierId;
        CarrierName = NormalizeText(carrierName);
        CarrierCode = NormalizeText(carrierCode);

        ContainerTypeId = containerTypeId;
        ContainerTypeCode = NormalizeText(containerTypeCode);
        Quantity = quantity;

        NormalizedOceanFreight = EnsureNonNegative(normalizedOceanFreight, nameof(normalizedOceanFreight));
        NormalizedAllIn = EnsureNonNegative(normalizedAllIn, nameof(normalizedAllIn));
        NormalizedCurrency = NormalizeText(normalizedCurrency)?.ToUpperInvariant();
        NormalizedAmount = EnsureNonNegative(normalizedAmount, nameof(normalizedAmount));
        ExchangeRate = EnsurePositive(exchangeRate, nameof(exchangeRate));
        ExchangeRateDate = exchangeRateDate.HasValue ? NormalizeUtc(exchangeRateDate.Value) : null;
        NormalizationConfidence = EnsureConfidence(normalizationConfidence, nameof(normalizationConfidence));
    }

    private static string RequireText(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(message);
        }

        return value.Trim();
    }

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal EnsureConfidence(decimal value, string fieldName)
    {
        if (value < 0m || value > 1m)
        {
            throw new InvalidOperationException($"{fieldName} debe estar entre 0 y 1.");
        }

        return value;
    }

    private static decimal? EnsureNonNegative(decimal? value, string fieldName)
    {
        if (value.HasValue && value.Value < 0m)
        {
            throw new InvalidOperationException($"{fieldName} no puede ser negativo.");
        }

        return value;
    }

    private static decimal? EnsurePositive(decimal? value, string fieldName)
    {
        if (value.HasValue && value.Value <= 0m)
        {
            throw new InvalidOperationException($"{fieldName} debe ser mayor que cero.");
        }

        return value;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
