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
        int quantity
    ) : base(id)
    {
        RateHeaderId = rateHeaderId;
        ContainerTypeId = containerTypeId;
        ContainerTypeName = containerTypeName.Trim();
        ContainerTypeCode = containerTypeCode.Trim();
        Quantity = quantity;
    }

    public Guid RateHeaderId { get; private set; }
    public Guid ContainerTypeId { get; private set; }
    public string ContainerTypeName { get; private set; } = string.Empty;
    public string ContainerTypeCode { get; private set; } = string.Empty;
    public int Quantity { get; private set; }

    internal static RateContainerAllocation Create(
        Guid rateHeaderId,
        Guid containerTypeId,
        string containerTypeName,
        string containerTypeCode,
        int quantity
    )
    {
        Validate(containerTypeId, containerTypeName, containerTypeCode, quantity);
        return new RateContainerAllocation(
            Guid.NewGuid(),
            rateHeaderId,
            containerTypeId,
            containerTypeName,
            containerTypeCode,
            quantity
        );
    }

    internal void Update(string containerTypeName, string containerTypeCode, int quantity)
    {
        Validate(ContainerTypeId, containerTypeName, containerTypeCode, quantity);
        ContainerTypeName = containerTypeName.Trim();
        ContainerTypeCode = containerTypeCode.Trim();
        Quantity = quantity;
    }

    private static void Validate(
        Guid containerTypeId,
        string containerTypeName,
        string containerTypeCode,
        int quantity
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
    int Quantity
);
