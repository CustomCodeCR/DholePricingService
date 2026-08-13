namespace Dhole.Pricing.Application.Abstractions.Services;

public sealed record PricingNotificationRecipient(
    Guid UserId,
    string? Email,
    string? DisplayName,
    string? UserName
);

public interface IPricingNotificationRecipientProvider
{
    Task<IReadOnlyCollection<PricingNotificationRecipient>> GetPricingRecipientsAsync(
        CancellationToken cancellationToken = default
    );
}
