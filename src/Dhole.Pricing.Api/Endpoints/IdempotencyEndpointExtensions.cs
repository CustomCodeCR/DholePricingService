namespace Dhole.Pricing.Api.Endpoints;

public sealed class IdempotentCreateMetadata
{
    public static IdempotentCreateMetadata Instance { get; } = new();

    private IdempotentCreateMetadata() { }
}

public static class IdempotencyEndpointExtensions
{
    public static RouteHandlerBuilder RequireIdempotency(this RouteHandlerBuilder builder)
    {
        return builder.WithMetadata(IdempotentCreateMetadata.Instance);
    }
}
