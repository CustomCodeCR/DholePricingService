using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.Features.Rates.SetRateStatus;

/// <summary>Only a distinct accepted quotation for the same CRM identity can win another QUO.</summary>
public static class RateAcceptedQuotationLinkPolicy
{
    public static bool CanLink(RateHeader source, RateHeader accepted)
    {
        if (source.Id == accepted.Id || source.IsDeleted || accepted.IsDeleted ||
            accepted.Status != RateStatus.AcceptedByClient)
            return false;

        if (source.ClientAccountId.HasValue || accepted.ClientAccountId.HasValue)
            return source.ClientAccountId.HasValue &&
                source.ClientAccountId == accepted.ClientAccountId;

        if (source.ClientLeadId.HasValue || accepted.ClientLeadId.HasValue)
            return source.ClientLeadId.HasValue &&
                source.ClientLeadId == accepted.ClientLeadId;

        return !string.IsNullOrWhiteSpace(source.ClientName) &&
            string.Equals(source.ClientName.Trim(), accepted.ClientName?.Trim(),
                StringComparison.OrdinalIgnoreCase);
    }
}
