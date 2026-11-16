namespace Dhole.Pricing.Api.Authorization;

internal static class ScopeAuthorizationExtensions
{
    private const string ScopePolicyPrefix = "Scope:";

    // Use the existing dynamic Scope: authorization policies, not raw JWT claims.
    // This keeps rate.view wizard reads compatible with the new LCL view permission.
    public static RouteHandlerBuilder RequireAnyScope(
        this RouteHandlerBuilder builder, params string[] scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var policies = scopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => $"{ScopePolicyPrefix}{scope.Trim()}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (policies.Length == 0)
            throw new ArgumentException("At least one scope is required.", nameof(scopes));

        builder.AddEndpointFilter(async (context, next) =>
        {
            var authorization = context.HttpContext.RequestServices
                .GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
            foreach (var policy in policies)
            {
                if ((await authorization.AuthorizeAsync(context.HttpContext.User, policy)).Succeeded)
                    return await next(context);
            }
            return Results.Forbid();
        });
        return builder;
    }

    public static TBuilder RequireScope<TBuilder>(this TBuilder builder, string scope)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        builder.RequireAuthorization($"{ScopePolicyPrefix}{scope.Trim()}");
        return builder;
    }
}
