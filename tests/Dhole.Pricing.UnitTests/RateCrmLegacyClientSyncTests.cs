using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateCrmLegacyClientSyncTests
{
    [TestMethod]
    public void LinkLegacyQuote_UpdatesVisibleClientAndExecutive_WithoutChangingCommercialState()
    {
        var rate = NewRate();
        var accountId = Guid.NewGuid();
        rate.ConfigureExecutive("Ejecutivo anterior");
        var status = rate.Status;
        var revision = rate.RevisionNumber;
        var sale = rate.TotalSaleAmount;
        var cost = rate.TotalCostAmount;

        Assert.IsTrue(rate.ReconcileLegacyClientAccount(accountId, "  SOCIEDAD ANONIMA  ", "Ejecutivo CRM"));
        Assert.AreEqual(accountId, rate.ClientAccountId);
        Assert.AreEqual("SOCIEDAD ANONIMA", rate.ClientName);
        Assert.AreEqual("Ejecutivo CRM", rate.ExecutiveName);
        StringAssert.Contains(rate.RateName, "SOCIEDAD ANONIMA");
        Assert.AreEqual(status, rate.Status);
        Assert.AreEqual(revision, rate.RevisionNumber);
        Assert.AreEqual(sale, rate.TotalSaleAmount);
        Assert.AreEqual(cost, rate.TotalCostAmount);
        Assert.IsFalse(rate.ReconcileLegacyClientAccount(accountId, "SOCIEDAD ANONIMA", "Ejecutivo CRM"));
    }

    [TestMethod]
    public void AlreadyLinkedQuote_CanSynchronizeItsVisibleCRMName()
    {
        var rate = NewRate();
        var accountId = Guid.NewGuid();
        rate.ConfigureClientAccount(accountId);

        Assert.IsTrue(rate.ReconcileLegacyClientAccount(accountId, "CLIENTE ACTUAL CRM", null));
        Assert.AreEqual("CLIENTE ACTUAL CRM", rate.ClientName);
        Assert.AreEqual(accountId, rate.ClientAccountId);
        Assert.IsFalse(rate.ReconcileLegacyClientAccount(accountId, "CLIENTE ACTUAL CRM", null));
    }

    [TestMethod]
    public void LinkLegacyQuote_CannotStealAnotherAccountOrProspect()
    {
        var linked = NewRate();
        linked.ConfigureClientAccount(Guid.NewGuid());
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            linked.ReconcileLegacyClientAccount(Guid.NewGuid(), "OTRA CUENTA", null));

        var lead = NewRate();
        lead.ConfigureClientLead(Guid.NewGuid());
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            lead.ReconcileLegacyClientAccount(Guid.NewGuid(), "CUENTA", null));
    }

    [TestMethod]
    public void LinkLegacyQuote_RequiresValidClientName()
    {
        var rate = NewRate();
        Assert.ThrowsExactly<ArgumentException>(() =>
            rate.ReconcileLegacyClientAccount(Guid.NewGuid(), " ", null));
        Assert.ThrowsExactly<ArgumentException>(() =>
            rate.ReconcileLegacyClientAccount(Guid.NewGuid(), new string('A', 221), null));
        Assert.IsNull(rate.ClientAccountId);
        Assert.AreEqual("CLIENTE HISTORICO", rate.ClientName);
    }

    [TestMethod]
    public void LinkLegacyQuoteToLead_UpdatesVisibleNameButNotCommercialState()
    {
        var rate = NewRate();
        var leadId = Guid.NewGuid();
        var status = rate.Status;
        var revision = rate.RevisionNumber;
        var sale = rate.TotalSaleAmount;
        var cost = rate.TotalCostAmount;
        Assert.IsTrue(rate.ReconcileLegacyClientLead(leadId, "  PROSPECTO CRM  ", "VENDEDOR CRM"));
        Assert.AreEqual(leadId, rate.ClientLeadId);
        Assert.AreEqual("PROSPECTO CRM", rate.ClientName);
        Assert.AreEqual("VENDEDOR CRM", rate.ExecutiveName);
        StringAssert.Contains(rate.RateName, "PROSPECTO CRM");
        Assert.AreEqual(status, rate.Status);
        Assert.AreEqual(revision, rate.RevisionNumber);
        Assert.AreEqual(sale, rate.TotalSaleAmount);
        Assert.AreEqual(cost, rate.TotalCostAmount);
        Assert.IsFalse(rate.ReconcileLegacyClientLead(leadId, "PROSPECTO CRM", "VENDEDOR CRM"));
    }

    [TestMethod]
    public void LeadReconciliation_CannotTakeAccountOrAnotherLead()
    {
        var account = NewRate();
        account.ConfigureClientAccount(Guid.NewGuid());
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            account.ReconcileLegacyClientLead(Guid.NewGuid(), "OTRO", null));
        var lead = NewRate();
        lead.ConfigureClientLead(Guid.NewGuid());
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            lead.ReconcileLegacyClientLead(Guid.NewGuid(), "OTRO", null));
    }

    [TestMethod]
    public void LeadReconciliation_RequiresValidNameAndIdentity()
    {
        var rate = NewRate();
        Assert.ThrowsExactly<ArgumentException>(() =>
            rate.ReconcileLegacyClientLead(Guid.Empty, "PROSPECTO", null));
        Assert.ThrowsExactly<ArgumentException>(() =>
            rate.ReconcileLegacyClientLead(Guid.NewGuid(), "  ", null));
        Assert.IsNull(rate.ClientLeadId);
    }

    private static RateHeader NewRate()
    {
        var today = DateTime.UtcNow.Date;
        return RateHeader.Create(
            rateCode: "QUO-A7K2P-9X4M8Q",
            sourceImportFclRateId: null,
            agentId: Guid.NewGuid(), agentName: "Agente", agentCode: "AGT",
            carrierId: Guid.NewGuid(), carrierName: "Naviera", carrierCode: "CAR",
            polId: Guid.NewGuid(), polName: "Shanghai", polCode: "CNSHA",
            poeId: Guid.NewGuid(), poeName: "Caldera", poeCode: "CRCAL",
            podId: Guid.NewGuid(), podName: "San José", podCode: "CRSJO",
            containerTypeId: Guid.NewGuid(), containerTypeName: "40HC", containerTypeCode: "40HC",
            incotermId: null, incotermName: null, incotermCode: null,
            currencyId: Guid.NewGuid(), currencyName: "Dólar", currencyCode: "USD",
            freeDays: 7, validFrom: today, validTo: today.AddDays(30),
            containerQuantity: 1, clientName: "CLIENTE HISTORICO",
            idtraNumber: null, quoNumber: "QUO-00000-000015",
            includes: null, subjectTo: null, excludes: null,
            transitTime: "20 días", rateType: RateType.Tariff, createdBy: null);
    }
}
