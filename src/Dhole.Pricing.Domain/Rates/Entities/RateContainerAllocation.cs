using CustomCodeFramework.Core.Domain.Entities;

namespace Dhole.Pricing.Domain.Rates.Entities;

public sealed class RateContainerAllocation : Entity<Guid>
{
    private RateContainerAllocation() { }

    private RateContainerAllocation(
        Guid id,
        Guid rateHeaderId,
        Guid containerTypeId,
        string containerTypeName,
        string containerTypeCode,
        int quantity,
        IReadOnlyList<decimal>? unitWeightsKg
    ) : base(id)
    {
        RateHeaderId = rateHeaderId;
        ContainerTypeId = containerTypeId;
        ContainerTypeName = containerTypeName.Trim();
        ContainerTypeCode = containerTypeCode.Trim();
        Quantity = quantity;
        UnitWeightsKgJson = System.Text.Json.JsonSerializer.Serialize(unitWeightsKg ?? []);
    }

    public Guid RateHeaderId { get; private set; }
    public Guid ContainerTypeId { get; private set; }
    public string ContainerTypeName { get; private set; } = string.Empty;
    public string ContainerTypeCode { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public string UnitWeightsKgJson { get; private set; } = "[]";

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public IReadOnlyList<decimal> UnitWeightsKg =>
        System.Text.Json.JsonSerializer.Deserialize<decimal[]>(UnitWeightsKgJson) ?? [];

    internal static RateContainerAllocation Create(
        Guid rateHeaderId,
        Guid containerTypeId,
        string containerTypeName,
        string containerTypeCode,
        int quantity,
        IReadOnlyList<decimal>? unitWeightsKg = null
    )
    {
        Validate(containerTypeId, containerTypeName, containerTypeCode, quantity, unitWeightsKg);
        return new RateContainerAllocation(
            Guid.NewGuid(),
            rateHeaderId,
            containerTypeId,
            containerTypeName,
            containerTypeCode,
            quantity,
            unitWeightsKg
        );
    }

    internal void Update(string containerTypeName, string containerTypeCode, int quantity, IReadOnlyList<decimal>? unitWeightsKg = null)
    {
        Validate(ContainerTypeId, containerTypeName, containerTypeCode, quantity, unitWeightsKg);
        ContainerTypeName = containerTypeName.Trim();
        ContainerTypeCode = containerTypeCode.Trim();
        Quantity = quantity;
        UnitWeightsKgJson = System.Text.Json.JsonSerializer.Serialize(unitWeightsKg ?? []);
    }

    private static void Validate(
        Guid containerTypeId,
        string containerTypeName,
        string containerTypeCode,
        int quantity,
        IReadOnlyList<decimal>? unitWeightsKg = null
    )
    {
        if (
            containerTypeId == Guid.Empty
            || string.IsNullOrWhiteSpace(containerTypeName)
            || string.IsNullOrWhiteSpace(containerTypeCode)
        )
        {
            throw new InvalidOperationException("El tipo de contenedor es obligatorio.");
        }

        if (unitWeightsKg is { Count: > 0 } &&
            (unitWeightsKg.Count != quantity || unitWeightsKg.Any(w => w < 0m || w > 100000m)))
            throw new InvalidOperationException("Indique un peso válido para cada unidad.");

        if (quantity <= 0)
        {
            throw new InvalidOperationException(
                "La cantidad por tipo de contenedor debe ser mayor que cero."
            );
        }
    }
}

public sealed record RateContainerAllocationSpec(
    Guid ContainerTypeId,
    string ContainerTypeName,
    string ContainerTypeCode,
    int Quantity,
    IReadOnlyList<decimal>? UnitWeightsKg = null
);
