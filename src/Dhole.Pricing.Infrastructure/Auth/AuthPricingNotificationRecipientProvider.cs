using System.Net.Http.Json;
using Dhole.Pricing.Application.Abstractions.Services;

namespace Dhole.Pricing.Infrastructure.Auth;

public sealed class AuthPricingNotificationRecipientProvider(HttpClient httpClient)
    : IPricingNotificationRecipientProvider
{
    public async Task<IReadOnlyCollection<PricingNotificationRecipient>> GetPricingRecipientsAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(
            "/api/internal/auth/pricing-notification-recipients",
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PricingNotificationRecipient[]>(cancellationToken: cancellationToken)
            ?? Array.Empty<PricingNotificationRecipient>();
    }
}
