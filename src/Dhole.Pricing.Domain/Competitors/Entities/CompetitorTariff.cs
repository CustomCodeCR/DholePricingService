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
        var normalized = (values ?? [])
            .Where(value => value != Guid.Empty)
            .Distinct()
            .ToArray();

        if (normalized.Length == 0)
            throw new InvalidOperationException($"Seleccione al menos un valor para {fieldName}.");

        return normalized;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
