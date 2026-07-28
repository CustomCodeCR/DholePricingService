namespace Dhole.Pricing.Contracts.Imports.Request;

public sealed record UpdateImportRateCatalogsRequest(
    Guid ImportProfileId,
    Guid PolId,
    Guid PoeId,
    Guid PodId,
    Guid CarrierId,
    Guid AgentId,
    Guid ContainerTypeId,
    Guid CurrencyId
);
