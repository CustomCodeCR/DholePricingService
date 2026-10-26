using Dhole.Pricing.Api.Endpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class OwnLclMaerskBalboaCostPolicyTests
{
    [TestMethod]
    public void MaerskBalboa_UsesExactlyFourExpectedCosts_Total912()
    {
        var charges = new (Guid Id, decimal Amount)[]
        {
            (Guid.Parse("396edf76-94d3-4d92-a533-aa6b8501044b"), 342m),
            (Guid.Parse("fb8606fc-3c0a-4673-ae4d-965b5cfb1fa0"), 35m),
            (Guid.Parse("523029b2-9303-499c-bb76-c7d48c697709"), 90m),
            (Guid.Parse("fd5dd670-ba90-44b4-9b49-1ce78d9480b8"), 400m),
            (Guid.Parse("c0543b87-9149-4fae-8571-219fff6e19dc"), 1000m),
            (Guid.Parse("a4bee4d6-8de0-4279-94b0-0279747d68fc"), 178m),
            (Guid.Parse("19cec538-ca34-4ddd-8e56-60b45a1272e4"), 80m),
            (Guid.Parse("aad761f1-622b-4d78-84e7-9619ebf3d55b"), 45m),
            (Guid.Parse("4c060a3b-4ba9-4350-8ec0-42a4f2e57bd2"), 5m),
        };

        var selected = OwnLclDestinationCostPolicy.SelectMaerskBalboaCharges(charges, x => x.Id);

        Assert.AreEqual(OwnLclDestinationCostPolicy.RequiredMaerskBalboaChargeCount, selected.Length);
        Assert.AreEqual(912m, selected.Sum(x => x.Amount));
        Assert.IsFalse(selected.Any(x => x.Amount is 35m or 1000m or 178m or 45m or 5m));
    }

    [TestMethod]
    public void Scope_OnlyAppliesToMaerskAndBalboa()
    {
        Assert.IsTrue(OwnLclDestinationCostPolicy.IsMaerskBalboaSelection(
            new[] { "MAERSK", "Maersk Line" }, "POE-2026-003"));
        Assert.IsTrue(OwnLclDestinationCostPolicy.IsMaerskBalboaSelection(
            new[] { "Maersk" }, "Balboa, Panama"));

        Assert.IsFalse(OwnLclDestinationCostPolicy.IsMaerskBalboaSelection(
            new[] { "MSC" }, "POE-2026-003"));
        Assert.IsFalse(OwnLclDestinationCostPolicy.IsMaerskBalboaSelection(
            new[] { "MAERSK" }, "CRCAL"));
        Assert.IsFalse(OwnLclDestinationCostPolicy.IsMaerskBalboaSelection(
            new[] { "MAERSK" }, "Puerto Moín"));
    }
}
