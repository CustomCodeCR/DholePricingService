using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Domain.Competitors.Entities;

public sealed class CompetitorTariff
{
    private CompetitorTariff() { }

    private CompetitorTariff(
        Guid id,
        IReadOnlyCollection<Guid> polIds,
        IReadOnlyCollection<Guid> poeIds,
        IReadOnlyCollection<Guid> podIds,
        IReadOnlyCollection<Guid> carrierIds,
        DateTime validFrom,
        DateTime validTo,
        ShipmentMode shipmentMode,
        Guid storageId
    )
    {
        Id = id;
        CompetitorCompanyName = "Competencia";
        ImportStatus = "Legacy";
        ImportedAtUtc = DateTime.UtcNow;
        Apply(polIds, poeIds, podIds, carrierIds, validFrom, validTo, shipmentMode, storageId);
    }

    public Guid Id { get; private set; }
    public Guid[] PolIds { get; private set; } = [];
    public Guid[] PoeIds { get; private set; } = [];
    public Guid[] PodIds { get; private set; } = [];
    public Guid[] CarrierIds { get; private set; } = [];
    public DateTime ValidFrom { get; private set; }
    public DateTime ValidTo { get; private set; }
    public ShipmentMode ShipmentMode { get; private set; }
    public Guid StorageId { get; private set; }

    public string CompetitorCompanyName { get; private set; } = "Competencia";
    public Guid? IncotermId { get; private set; }
    public string? OriginalFileName { get; private set; }
    public Guid? ExtractionExecutionId { get; private set; }
    public int ObservationCount { get; private set; }
    public int ReviewCount { get; private set; }
    public string ImportStatus { get; private set; } = "Legacy";
    public DateTime ImportedAtUtc { get; private set; }

    public static CompetitorTariff Create(
        Guid id,
        IReadOnlyCollection<Guid> polIds,
        IReadOnlyCollection<Guid> poeIds,
        IReadOnlyCollection<Guid> podIds,
        IReadOnlyCollection<Guid> carrierIds,
        DateTime validFrom,
        DateTime validTo,
        ShipmentMode shipmentMode,
        Guid storageId
    )
    {
        if (id == Guid.Empty)
            throw new InvalidOperationException("El identificador del tarifario de competencia es obligatorio.");

        return new CompetitorTariff(
            id,
            polIds,
            poeIds,
            podIds,
            carrierIds,
            validFrom,
            validTo,
            shipmentMode,
            storageId
        );
    }

    public static CompetitorTariff CreateImport(
        Guid id,
        string competitorCompanyName,
        Guid incotermId,
        DateTime validFrom,
        DateTime validTo,
        ShipmentMode shipmentMode,
        Guid storageId,
        string originalFileName
    )
    {
        if (id == Guid.Empty)
            throw new InvalidOperationException("El identificador del tarifario de competencia es obligatorio.");

        if (incotermId == Guid.Empty)
            throw new InvalidOperationException("El Incoterm es obligatorio para alimentar Average.");

        if (!Enum.IsDefined(shipmentMode))
            throw new InvalidOperationException("La modalidad del tarifario de competencia no es válida.");

        if (storageId == Guid.Empty)
            throw new InvalidOperationException("El archivo almacenado del tarifario de competencia es obligatorio.");

        var fromUtc = NormalizeUtc(validFrom);
        var toUtc = NormalizeUtc(validTo);
        if (toUtc < fromUtc)
            throw new InvalidOperationException("La vigencia hasta no puede ser anterior a la vigencia desde.");

        return new CompetitorTariff
        {
            Id = id,
            PolIds = [],
            PoeIds = [],
            PodIds = [],
            CarrierIds = [],
            ValidFrom = fromUtc,
            ValidTo = toUtc,
            ShipmentMode = shipmentMode,
            StorageId = storageId,
            CompetitorCompanyName = RequireText(
                competitorCompanyName,
                "El competidor o fuente es obligatorio."
            ),
            IncotermId = incotermId,
            OriginalFileName = RequireText(
                originalFileName,
                "El nombre original del archivo es obligatorio."
            ),
            ImportStatus = "Processing",
            ImportedAtUtc = DateTime.UtcNow,
        };
    }

    public void Update(
        IReadOnlyCollection<Guid> polIds,
        IReadOnlyCollection<Guid> poeIds,
        IReadOnlyCollection<Guid> podIds,
        IReadOnlyCollection<Guid> carrierIds,
        DateTime validFrom,
        DateTime validTo,
        ShipmentMode shipmentMode,
        Guid storageId
    ) => Apply(polIds, poeIds, podIds, carrierIds, validFrom, validTo, shipmentMode, storageId);

    public void CompleteImport(
        IReadOnlyCollection<Guid> polIds,
        IReadOnlyCollection<Guid> poeIds,
        IReadOnlyCollection<Guid> podIds,
        IReadOnlyCollection<Guid> carrierIds,
        DateTime validFrom,
        DateTime validTo,
        Guid? extractionExecutionId,
        int observationCount,
        int reviewCount,
        string importStatus
    )
    {
        var fromUtc = NormalizeUtc(validFrom);
        var toUtc = NormalizeUtc(validTo);
        if (toUtc < fromUtc)
            throw new InvalidOperationException("La vigencia hasta no puede ser anterior a la vigencia desde.");

        PolIds = NormalizeOptionalIds(polIds);
        PoeIds = NormalizeOptionalIds(poeIds);
        PodIds = NormalizeOptionalIds(podIds);
        CarrierIds = NormalizeOptionalIds(carrierIds);
        ValidFrom = fromUtc;
        ValidTo = toUtc;
        ExtractionExecutionId = extractionExecutionId;
        ObservationCount = Math.Max(0, observationCount);
        ReviewCount = Math.Max(0, reviewCount);
        ImportStatus = RequireText(importStatus, "El estado de importación es obligatorio.");
    }

    public void MarkImportFailed(Guid? extractionExecutionId, int reviewCount)
    {
        ExtractionExecutionId = extractionExecutionId;
        ObservationCount = 0;
        ReviewCount = Math.Max(0, reviewCount);
        ImportStatus = "Failed";
    }

    private void Apply(
        IReadOnlyCollection<Guid> polIds,
        IReadOnlyCollection<Guid> poeIds,
        IReadOnlyCollection<Guid> podIds,
        IReadOnlyCollection<Guid> carrierIds,
        DateTime validFrom,
        DateTime validTo,
        ShipmentMode shipmentMode,
        Guid storageId
    )
    {
        PolIds = NormalizeIds(polIds, "POL");
        PoeIds = NormalizeIds(poeIds, "POE");
        PodIds = NormalizeIds(podIds, "POD");
        CarrierIds = NormalizeIds(carrierIds, "naviera");

        if (!Enum.IsDefined(shipmentMode))
            throw new InvalidOperationException("La modalidad del tarifario de competencia no es válida.");

        var fromUtc = NormalizeUtc(validFrom);
        var toUtc = NormalizeUtc(validTo);
        if (toUtc < fromUtc)
            throw new InvalidOperationException("La vigencia hasta no puede ser anterior a la vigencia desde.");

        if (storageId == Guid.Empty)
            throw new InvalidOperationException("El archivo almacenado del tarifario de competencia es obligatorio.");

        ValidFrom = fromUtc;
        ValidTo = toUtc;
        ShipmentMode = shipmentMode;
        StorageId = storageId;
    }

    private static Guid[] NormalizeIds(
        IReadOnlyCollection<Guid>? values,
        string fieldName
    )
    {
        var normalized = NormalizeOptionalIds(values);

        if (normalized.Length == 0)
            throw new InvalidOperationException($"Seleccione al menos un valor para {fieldName}.");

        return normalized;
    }

    private static Guid[] NormalizeOptionalIds(IReadOnlyCollection<Guid>? values) =>
        (values ?? [])
            .Where(value => value != Guid.Empty)
            .Distinct()
            .ToArray();

    private static string RequireText(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(message);

        return value.Trim();
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
