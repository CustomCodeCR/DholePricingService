using System.Security.Cryptography;
using System.Text;
using Dhole.Pricing.Domain.Imports.Entities;

namespace Dhole.Pricing.Application.Imports;

internal static class ImportedCatalogSnapshot
{
    public static bool CanOverrideAgent(ImportFclRates importedRate)
    {
        return IsUnresolved(
            importedRate.AgentId,
            importedRate.AgentName,
            importedRate.AgentCode,
            importedRate.AgentSlug,
            "agents"
        );
    }

    public static bool CanOverridePod(ImportFclRates importedRate)
    {
        return IsUnresolved(
            importedRate.PodId,
            importedRate.PodName,
            importedRate.PodCode,
            importedRate.PodSlug,
            "pod"
        );
    }

    private static bool IsUnresolved(
        Guid id,
        string? name,
        string? code,
        string? slug,
        string catalogGroupSlug
    )
    {
        if (
            string.Equals(code, "PENDING", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Por asignar", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(slug)
        )
        {
            return true;
        }

        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{catalogGroupSlug}:{slug.Trim().ToLowerInvariant()}")
        );
        var fallbackId = new Guid(hash.AsSpan(0, 16));

        return id == fallbackId;
    }
}