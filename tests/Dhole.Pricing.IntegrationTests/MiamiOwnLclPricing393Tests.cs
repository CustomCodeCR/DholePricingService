using Dhole.Pricing.Api.Endpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class MiamiOwnLclPricing393Tests
{
    private static OwnLclConsolidationDto Consolidation() => new(
        Guid.Parse("4d49414d-4900-4000-8000-000000000001"),
        48, "Consolidado Miami", null, null,
        null, null, null, null, null, null,
        null, "Miami, Estados Unidos", "USMIA",
        null, "Moín, Costa Rica", "CRPMN",
        null, "San José, Costa Rica", "CRSJO",
        0m, 50m, 0m, 0m, 0m, 50m, 5.69m,
        "MIA-SYSTEM-v2", "Open", true);

    private static CalculateOwnLclQuoteRequest Request(string plan, decimal cargoValue = 0m,
        bool includeSed = true, int sedQty = 1, int whs = 1, bool dangerous = false) =>
        new("CR", "EXW",
            [new OwnLclCargoLineRequest("Carga", 1, 10m, 100m, 100m, 100m)],
            CommercialPlan: plan, CargoValue: cargoValue, IncludeSed: includeSed,
            SedQty: sedQty, WhsQty: whs, IsDangerousCargo: dangerous);

    private static OwnLclQuoteCalculationDto Calculate(
        CalculateOwnLclQuoteRequest request, params CargoCalculationLine[] cargo)
    {
        var valid = MiamiOwnLclPricing.TryCalculate(
            Consolidation(), request,
            cargo.Length > 0 ? cargo : [new("Carga", 1, 10m, 1m, 0.02m, 1m)],
            "MIAMI", "CR", request.Incoterm, out var quote, out var error);
        Assert.IsTrue(valid, error);
        Assert.IsNotNull(quote);
        return quote!;
    }

    [TestMethod]
    public void ClientD_UsesReferenceSaleMinimumAndAllFakLines()
    {
        var quote = Calculate(Request("D"), new("Carga", 1, 10m, 1m, 0.02m, 1m));
        // 35.3146667 CFT × USD 2.92 = USD 103.12; the USD 292 minimum wins.
        Assert.AreEqual(292m, quote.Lines.Single(x => x.Name == "Flete internacional marítimo").SaleTotal);
        Assert.AreEqual(35m, quote.Lines.Single(x => x.Name == "SED").SaleTotal);
        Assert.AreEqual(55m, quote.Lines.Single(x => x.Name == "Manejos").SaleTotal);
        Assert.AreEqual(45m, quote.Lines.Single(x => x.Name == "VGM").SaleTotal);
        Assert.AreEqual(65m, quote.Lines.Single(x => x.Name == "Forwarding").SaleTotal);
        Assert.AreEqual(607m, quote.SubtotalSale); // USD 292 + 35+55+45+30+35+50+65
    }

    [TestMethod]
    public void ClientA_ComparesOverallWeightAgainstOverallVolume_AndSedQuantity()
    {
        // Each cargo line is NOT billed independently with max(weight,volume).
        var cargo = new[] {
            new CargoCalculationLine("Bulky", 1, 1m, 2m, 0.002m, 2m),
            new CargoCalculationLine("Heavy", 1, 300m, 0.01m, 0.60m, 0.60m)
        };
        var quote = Calculate(Request("A", includeSed: true, sedQty: 2), cargo);
        Assert.IsTrue(Math.Abs(quote.BillableCbm - 2.01m) < .0001m);
        Assert.AreEqual(50m, quote.Lines.Single(x => x.Name == "SED").SaleTotal);
        Assert.AreEqual("Flat", quote.Lines.Single(x => x.Name == "Flete internacional marítimo").ChargeBasis);
    }

    [TestMethod]
    public void NvoccA_RespectsMinimumAndInsurance()
    {
        var quote = Calculate(Request("NVOCC-A", cargoValue: 1000m),
            new("Carga", 1, 8m, .01m, .016m, .01m));
        Assert.AreEqual(95m, quote.Lines.Single(x => x.Name == "Flete internacional marítimo").SaleTotal);
        Assert.AreEqual(50m, quote.Lines.Single(x => x.Name == "Seguro de carga").SaleTotal);
    }

    [TestMethod]
    public void Little_RejectsImoAndOversizeAndBillsVolumeOnly()
    {
        var invalid = MiamiOwnLclPricing.TryCalculate(
            Consolidation(), Request("LITTLE", dangerous: true),
            [new CargoCalculationLine("IMO", 1, 20m, .50m, .04m, .50m)],
            "MIAMI", "CR", "EXW", out _, out var message);
        Assert.IsFalse(invalid);
        StringAssert.Contains(message!, "IMO");

        var quote = Calculate(Request("LITTLE"), new("Paquete", 1, 90m, .25m, .18m, .25m));
        Assert.AreEqual(30m, quote.Lines.Single(x => x.Name == "Flete internacional marítimo").SaleTotal);
        Assert.AreEqual(0m, quote.Lines.Where(x => x.Name == "SED").Sum(x => x.SaleTotal));
    }
}
