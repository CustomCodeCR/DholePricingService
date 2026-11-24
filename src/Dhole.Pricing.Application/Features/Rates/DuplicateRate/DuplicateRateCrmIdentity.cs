using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.Application.Features.Rates.DuplicateRate;

/// <summary>Preserve the source identity on ordinary copies; only an applied tariff uses the selected CRM identity.</summary>
public static class DuplicateRateCrmIdentity
{
    public static void Copy(RateHeader source, RateHeader target, DuplicateRateCommand command)
    {
        var accountId = command.ApplyTariff ? command.ClientAccountId : source.ClientAccountId;
        var leadId = command.ApplyTariff ? command.ClientLeadId : source.ClientLeadId;
        if (accountId is Guid account) target.ConfigureClientAccount(account);
        if (leadId is Guid lead) target.ConfigureClientLead(lead);
    }
}
