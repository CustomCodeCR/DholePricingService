using System.Net.Http.Json;
using Dhole.Pricing.Application.Abstractions.Services;

namespace Dhole.Pricing.Infrastructure.Auth;

public sealed class AuthPricingNotificationRecipientProvider(HttpClient httpClient)
    : IPricingNotificationRecipientProvider
{
    public Task<IReadOnlyCollection<PricingNotificationRecipient>> GetPricingRecipientsAsync(
        CancellationToken cancellationToken = default)
        => GetAsync("/api/internal/auth/pricing-notification-recipients", cancellationToken);

    public Task<IReadOnlyCollection<PricingNotificationRecipient>> GetRecipientsByScopeAsync(
        string requiredScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredScope);

        var encodedScope = Uri.EscapeDataString(requiredScope.Trim());
        return GetAsync(
            $"/api/internal/auth/pricing-notification-recipients?requiredScope={encodedScope}",
            cancellationToken
        );
    }

    private async Task<IReadOnlyCollection<PricingNotificationRecipient>> GetAsync(
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PricingNotificationRecipient[]>(
            cancellationToken: cancellationToken)
            ?? Array.Empty<PricingNotificationRecipient>();
    }
}
