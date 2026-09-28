using CustomCodeFramework.Core.Domain.Entities;

namespace Dhole.Pricing.Domain.MarketPricing.Entities;

public sealed class AutoPricingProfile : Entity<Guid>
{
    private AutoPricingProfile() { }

    private AutoPricingProfile(
        Guid id,
        string name,
        string code,
        decimal targetPercentile,
        decimal competitiveCeilingPercentile,
        decimal minimumConfidenceForAutoApply,
        decimal minimumConfidenceForSuggestion,
        int minimumCompetitorCount,
        int minimumObservationCount,
        decimal maximumMarketDeviation,
        bool isActive
    ) : base(id)
    {
        Apply(
            name,
            code,
            targetPercentile,
            competitiveCeilingPercentile,
            minimumConfidenceForAutoApply,
            minimumConfidenceForSuggestion,
            minimumCompetitorCount,
            minimumObservationCount,
            maximumMarketDeviation,
            isActive
        );
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;

    public decimal TargetPercentile { get; private set; }
    public decimal CompetitiveCeilingPercentile { get; private set; }

    public decimal MinimumConfidenceForAutoApply { get; private set; }
    public decimal MinimumConfidenceForSuggestion { get; private set; }

    public int MinimumCompetitorCount { get; private set; }
    public int MinimumObservationCount { get; private set; }

    public decimal MaximumMarketDeviation { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public static AutoPricingProfile Create(
        string name,
        string code,
        decimal targetPercentile = 60m,
        decimal competitiveCeilingPercentile = 65m,
        decimal minimumConfidenceForAutoApply = 80m,
        decimal minimumConfidenceForSuggestion = 60m,
        int minimumCompetitorCount = 3,
        int minimumObservationCount = 3,
        decimal maximumMarketDeviation = 25m,
        bool isActive = true
    ) => new(
        Guid.NewGuid(),
        name,
        code,
        targetPercentile,
        competitiveCeilingPercentile,
        minimumConfidenceForAutoApply,
        minimumConfidenceForSuggestion,
        minimumCompetitorCount,
        minimumObservationCount,
        maximumMarketDeviation,
        isActive
    );

    public void Update(
        string name,
        decimal targetPercentile,
        decimal competitiveCeilingPercentile,
        decimal minimumConfidenceForAutoApply,
        decimal minimumConfidenceForSuggestion,
        int minimumCompetitorCount,
        int minimumObservationCount,
        decimal maximumMarketDeviation,
        bool isActive
    )
    {
        Apply(
            name,
            Code,
            targetPercentile,
            competitiveCeilingPercentile,
            minimumConfidenceForAutoApply,
            minimumConfidenceForSuggestion,
            minimumCompetitorCount,
            minimumObservationCount,
            maximumMarketDeviation,
            isActive
        );
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private void Apply(
        string name,
        string code,
        decimal targetPercentile,
        decimal competitiveCeilingPercentile,
        decimal minimumConfidenceForAutoApply,
        decimal minimumConfidenceForSuggestion,
        int minimumCompetitorCount,
        int minimumObservationCount,
        decimal maximumMarketDeviation,
        bool isActive
    )
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("El perfil de auto pricing requiere nombre y código.");
        }

        ValidatePercent(targetPercentile, nameof(targetPercentile));
        ValidatePercent(competitiveCeilingPercentile, nameof(competitiveCeilingPercentile));
        ValidatePercent(minimumConfidenceForAutoApply, nameof(minimumConfidenceForAutoApply));
        ValidatePercent(minimumConfidenceForSuggestion, nameof(minimumConfidenceForSuggestion));
        ValidatePercent(maximumMarketDeviation, nameof(maximumMarketDeviation));

        if (competitiveCeilingPercentile < targetPercentile)
        {
            throw new InvalidOperationException("El percentil techo no puede ser menor que el percentil objetivo.");
        }

        if (minimumConfidenceForAutoApply < minimumConfidenceForSuggestion)
        {
            throw new InvalidOperationException("La confianza para auto aplicar no puede ser menor que la confianza para sugerir.");
        }

        if (minimumCompetitorCount < 1 || minimumObservationCount < 1)
        {
            throw new InvalidOperationException("Los mínimos de competidores y observaciones deben ser mayores que cero.");
        }

        Name = name.Trim();
        Code = code.Trim().ToUpperInvariant();
        TargetPercentile = targetPercentile;
        CompetitiveCeilingPercentile = competitiveCeilingPercentile;
        MinimumConfidenceForAutoApply = minimumConfidenceForAutoApply;
        MinimumConfidenceForSuggestion = minimumConfidenceForSuggestion;
        MinimumCompetitorCount = minimumCompetitorCount;
        MinimumObservationCount = minimumObservationCount;
        MaximumMarketDeviation = maximumMarketDeviation;
        IsActive = isActive;
    }

    private static void ValidatePercent(decimal value, string fieldName)
    {
        if (value < 0m || value > 100m)
        {
            throw new InvalidOperationException($"{fieldName} debe estar entre 0 y 100.");
        }
    }
}
