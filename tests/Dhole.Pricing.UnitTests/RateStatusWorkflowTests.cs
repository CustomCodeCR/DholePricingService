using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateStatusWorkflowTests
{
    [TestMethod]
    public void SetAmounts_MarginAtLeastTwelve_LeavesRateOpen()
    {
        var rate = CreateRate();
        AddFreight(rate, cost: 88m, sale: 100m);

        rate.SetAmounts(updatedBy: null);

        Assert.AreEqual(12m, rate.MarginPercentage);
        Assert.AreEqual(RateStatus.Open, rate.Status);
        Assert.IsFalse(rate.RequiredApproval);
    }

    [TestMethod]
    public void SetAmounts_MarginBelowTwelve_RequiresManagementApproval()
    {
        var rate = CreateRate();
        AddFreight(rate, cost: 90m, sale: 100m);

        rate.SetAmounts(updatedBy: null);

        Assert.AreEqual(10m, rate.MarginPercentage);
        Assert.AreEqual(RateStatus.PendingApproval, rate.Status);
        Assert.IsTrue(rate.RequiredApproval);
    }

    [TestMethod]
    public void ApproveLowMargin_RequiresOpeningBeforeItCanBeSent()
    {
        var rate = CreateRate();
        AddFreight(rate, cost: 90m, sale: 100m);
        rate.SetAmounts(updatedBy: null);

        rate.SetApprovalMargin(updatedBy: null, isApproved: true);
        Assert.AreEqual(RateStatus.ApprovedByManagement, rate.Status);
        Assert.IsFalse(rate.RequiredApproval);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            rate.SetCommercialStatus(RateStatus.Sent, reason: null, updatedBy: null)
        );

        rate.SetCommercialStatus(RateStatus.Open, reason: null, updatedBy: null);
        Assert.AreEqual(RateStatus.Open, rate.Status);

        rate.SetCommercialStatus(RateStatus.Sent, reason: null, updatedBy: null);
        Assert.AreEqual(RateStatus.Sent, rate.Status);
    }

    [TestMethod]
    public void AutomaticallyApproveLowMargin_ByScope_LeavesRateOpen()
    {
        var rate = CreateRate();
        AddFreight(rate, cost: 90m, sale: 100m);
        rate.SetAmounts(updatedBy: null);

        rate.SetApprovalMargin(
            updatedBy: null,
            isApproved: true,
            openAfterAutomaticApproval: true
        );

        Assert.AreEqual(RateStatus.Open, rate.Status);
        Assert.IsFalse(rate.RequiredApproval);
    }

    [TestMethod]
    public void RejectLowMargin_UsesManagementRejection_AndCannotBeSent()
    {
        var rate = CreateRate();
        AddFreight(rate, cost: 90m, sale: 100m);
        rate.SetAmounts(updatedBy: null);
        rate.SetApprovalMargin(updatedBy: null, isApproved: false);

        Assert.AreEqual(RateStatus.RejectedByManagement, rate.Status);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            rate.SetCommercialStatus(RateStatus.Sent, reason: null, updatedBy: null)
        );
    }

    [TestMethod]
    public void CommercialFlow_MustMoveFromOpenToSentBeforeClientDecision()
    {
        var rate = CreateRate();
        AddFreight(rate, cost: 80m, sale: 100m);
        rate.SetAmounts(updatedBy: null);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            rate.SetCommercialStatus(RateStatus.AcceptedByClient, reason: null, updatedBy: null)
        );

        rate.SetCommercialStatus(RateStatus.Sent, reason: null, updatedBy: null);
        rate.SetCommercialStatus(RateStatus.AcceptedByClient, reason: null, updatedBy: null);

        Assert.AreEqual(RateStatus.AcceptedByClient, rate.Status);
    }

    [TestMethod]
    public void SentRate_CanBeMarkedRequestedByClient_WithoutClientDecision()
    {
        var rate = CreateRate();
        AddFreight(rate, cost: 80m, sale: 100m);
        rate.SetAmounts(updatedBy: null);
        rate.SetCommercialStatus(RateStatus.Sent, reason: null, updatedBy: null);

        rate.SetCommercialStatus(RateStatus.RequestedByClient, reason: null, updatedBy: null);

        Assert.AreEqual(RateStatus.RequestedByClient, rate.Status);
        Assert.AreNotEqual(RateStatus.AcceptedByClient, rate.Status);
        Assert.AreNotEqual(RateStatus.RejectedByClient, rate.Status);
    }

    [TestMethod]
    public void RequestedByClient_CanLaterBeAccepted()
    {
        var rate = CreateRate();
        AddFreight(rate, cost: 80m, sale: 100m);
        rate.SetAmounts(updatedBy: null);
        rate.SetCommercialStatus(RateStatus.Sent, reason: null, updatedBy: null);
        rate.SetCommercialStatus(RateStatus.RequestedByClient, reason: null, updatedBy: null);

        rate.SetCommercialStatus(RateStatus.AcceptedByClient, reason: null, updatedBy: null);

        Assert.AreEqual(RateStatus.AcceptedByClient, rate.Status);
    }

    [TestMethod]
    public void CloseRate_RequiresReason()
    {
        var rate = CreateRate();
        AddFreight(rate, cost: 80m, sale: 100m);
        rate.SetAmounts(updatedBy: null);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            rate.SetCommercialStatus(RateStatus.Closed, reason: " ", updatedBy: null)
        );
    }

    [TestMethod]
    public void CloseRate_PersistsReasonAndAuditMetadata()
    {
        var userId = Guid.NewGuid();
        var rate = CreateRate();
        AddFreight(rate, cost: 80m, sale: 100m);
        rate.SetAmounts(updatedBy: null);

        rate.SetCommercialStatus(
            RateStatus.Closed,
            reason: "El cliente pospuso el embarque.",
            updatedBy: userId
        );

        Assert.AreEqual(RateStatus.Closed, rate.Status);
        Assert.AreEqual("El cliente pospuso el embarque.", rate.ClosedReason);
        Assert.IsNotNull(rate.ClosedAtUtc);
        Assert.AreEqual(userId, rate.ClosedBy);
    }

    [TestMethod]
    public void SetAmounts_WithIdtraAndQuo_AutomaticallyAcceptsRate()
    {
        var rate = CreateRate(idtraNumber: "IDTRA-2026-00125", quoNumber: "QUO-2026-00458");
        AddFreight(rate, cost: 95m, sale: 100m);

        rate.SetAmounts(updatedBy: null);

        Assert.AreEqual(RateStatus.AcceptedByClient, rate.Status);
        Assert.IsFalse(rate.RequiredApproval);
    }

    [TestMethod]
    public void SetAmounts_WithOnlyOneCommercialIdentifier_DoesNotAutoAccept()
    {
        var rate = CreateRate(idtraNumber: "IDTRA-2026-00125", quoNumber: null);
        AddFreight(rate, cost: 80m, sale: 100m);

        rate.SetAmounts(updatedBy: null);

        Assert.AreEqual(RateStatus.Open, rate.Status);
    }

    private static void AddFreight(RateHeader rate, decimal cost, decimal sale)
    {
        rate.AddRateDetail(
            rate.Id,
            costId: null,
            name: "Flete internacional",
            costDetailType: CostDetailType.Freight,
            costType: CostType.Variable,
            currencyId: rate.CurrencyId,
            currencyName: rate.CurrencyName,
            currencyCode: rate.CurrencyCode,
            costAmount: cost,
            saleAmount: sale,
            notes: null,
            quantity: 1,
            updatedBy: null
        );
    }

    [TestMethod]
    public void MarkExpired_WhenValidityEnded_ChangesActiveRateToExpired()
    {
        var today = DateTime.UtcNow.Date;
        var rate = CreateRate(validTo: today.AddDays(-1));
        AddFreight(rate, cost: 80m, sale: 100m);
        rate.SetAmounts(updatedBy: null);

        var changed = rate.MarkExpired(today);

        Assert.IsTrue(changed);
        Assert.AreEqual(RateStatus.Expired, rate.Status);
        Assert.IsFalse(rate.RequiredApproval);
    }

    [TestMethod]
    public void MarkExpired_DoesNotReplaceClosedDecision()
    {
        var today = DateTime.UtcNow.Date;
        var rate = CreateRate(validTo: today.AddDays(-1));
        AddFreight(rate, cost: 80m, sale: 100m);
        rate.SetAmounts(updatedBy: null);
        rate.SetCommercialStatus(
            RateStatus.Closed,
            reason: "La operación fue cerrada antes de vencer.",
            updatedBy: null
        );

        var changed = rate.MarkExpired(today);

        Assert.IsFalse(changed);
        Assert.AreEqual(RateStatus.Closed, rate.Status);
    }

    [TestMethod]
    public void Create_WithRandomQuoFormat_PreservesGeneratedCode()
    {
        var rate = CreateRate();

        Assert.AreEqual("QUO-A7K2P-9X4M8Q", rate.RateCode);
    }

    [TestMethod]
    public void Create_WithInvalidQuoFormat_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => CreateRate("QUO-A7K2!-9X4M8Q"));
    }

    [TestMethod]
    public void Create_WithLowercaseQuo_NormalizesToUppercase()
    {
        var rate = CreateRate("quo-a7k2p-9x4m8q");

        Assert.AreEqual("QUO-A7K2P-9X4M8Q", rate.RateCode);
    }

    private static RateHeader CreateRate(
        string rateCode = "QUO-A7K2P-9X4M8Q",
        string? idtraNumber = null,
        string? quoNumber = null,
        DateTime? validTo = null
    )
    {
        var today = DateTime.UtcNow.Date;
        return RateHeader.Create(
            rateCode,
            sourceImportFclRateId: null,
            agentId: Guid.NewGuid(),
            agentName: "Agente",
            agentCode: "AGT",
            carrierId: Guid.NewGuid(),
            carrierName: "Naviera",
            carrierCode: "CAR",
            polId: Guid.NewGuid(),
            polName: "Shanghai",
            polCode: "CNSHA",
            poeId: Guid.NewGuid(),
            poeName: "Caldera",
            poeCode: "CRCAL",
            podId: Guid.NewGuid(),
            podName: "San José",
            podCode: "CRSJO",
            containerTypeId: Guid.NewGuid(),
            containerTypeName: "40HC",
            containerTypeCode: "40HC",
            incotermId: null,
            incotermName: null,
            incotermCode: null,
            currencyId: Guid.NewGuid(),
            currencyName: "Dólar",
            currencyCode: "USD",
            freeDays: 7,
            validFrom: today,
            validTo: validTo ?? today.AddDays(30),
            containerQuantity: 1,
            clientName: "Cliente",
            idtraNumber,
            quoNumber,
            includes: null,
            subjectTo: null,
            excludes: null,
            transitDays: 20,
            createdBy: null
        );
    }
}
