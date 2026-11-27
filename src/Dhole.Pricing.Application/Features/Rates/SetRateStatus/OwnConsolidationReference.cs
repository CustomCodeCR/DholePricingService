using System.Text.RegularExpressions;

namespace Dhole.Pricing.Application.Features.Rates.SetRateStatus;

/// <summary>Explicit quote links only; never infer a departure from its route.</summary>
public static class OwnConsolidationReference
{
    public static (Guid? Id, int? Number) Read(IEnumerable<string?> notes)
    {
        var ids = new HashSet<Guid>();
        var numbers = new HashSet<int>();
        foreach (var note in notes)
        {
            if (string.IsNullOrWhiteSpace(note)) continue;
            foreach (Match match in Regex.Matches(note, @"ConsolidadoId:\s*([0-9a-fA-F-]{36})", RegexOptions.IgnoreCase))
                if (Guid.TryParse(match.Groups[1].Value, out var id) && id != Guid.Empty) ids.Add(id);
            foreach (Match match in Regex.Matches(note, @"Consolidado:\s*#(\d+)", RegexOptions.IgnoreCase))
                if (int.TryParse(match.Groups[1].Value, out var number) && number > 0) numbers.Add(number);
        }
        if (ids.Count > 1) return (null, null);
        return (ids.Count == 1 ? ids.Single() : null, numbers.Count == 1 ? numbers.Single() : null);
    }
}
