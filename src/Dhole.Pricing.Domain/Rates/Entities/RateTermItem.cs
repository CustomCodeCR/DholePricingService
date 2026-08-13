using CustomCodeFramework.Core.Domain.Entities;

namespace Dhole.Pricing.Domain.Rates.Entities;

public sealed class RateTermItem : Entity<Guid>
{
    private RateTermItem() { }

    private RateTermItem(Guid id, string text, int sortOrder) : base(id)
    {
        Text = Normalize(text);
        SortOrder = Math.Max(0, sortOrder);
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string Text { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public static RateTermItem Create(string text, int sortOrder = 0)
        => new(Guid.NewGuid(), text, sortOrder);

    public void Update(string text, int sortOrder, bool isActive)
    {
        Text = Normalize(text);
        SortOrder = Math.Max(0, sortOrder);
        IsActive = isActive;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("El texto del ítem tarifario es requerido.");
        return value.Trim();
    }
}
