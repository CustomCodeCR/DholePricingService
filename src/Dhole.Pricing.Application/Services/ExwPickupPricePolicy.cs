namespace Dhole.Pricing.Application.Services;

/// <summary>
/// A fixed catalog pickup can be used as the commercial EXW origin service.
/// When Pricing explicitly prices that pickup, the entered cost must not be
/// overwritten with the catalog base while the new RateDetail is created.
/// All other fixed costs retain their established catalog pricing behavior.
/// </summary>
public static class ExwPickupPricePolicy
{
    public const string ManualPriceMarker = "Recolecta EXW: precio autorizado en Pricing";

    public static decimal ResolveFixedCostAmount(
        decimal requestedAmount,
        decimal catalogAmount,
        bool updatingPersistedDetail,
        bool selectedCurrencyDiffers,
        string? notes)
    {
        var authorizedPickup = !string.IsNullOrWhiteSpace(notes)
            && notes.Contains(ManualPriceMarker, StringComparison.OrdinalIgnoreCase);

        return updatingPersistedDetail || selectedCurrencyDiffers || authorizedPickup
            ? requestedAmount
            : catalogAmount;
    }
}
