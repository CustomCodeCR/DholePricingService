using Dhole.Pricing.Domain.Rates;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class PanamaCostaRicaAllInPolicyTests
{
    [DataTestMethod]
    [DataRow("Rodman, Panamá", "PAPAN")]
    [DataRow("Balboa, Panamá", "PABLB")]
    [DataRow("Multimodal Vía Panamá", "MULTIMODAL_VIA_PANAMA")]
    public void PanamaFclToCostaRica_UsesAllIn(string poeName, string poeCode)
    {
        Assert.IsTrue(PanamaCostaRicaAllInPolicy.Applies(
            "Fcl", poeCode, poeName, "CRSJO", "San José, Costa Rica"));
    }

    [DataTestMethod]
    [DataRow("Puerto Caldera, Costa Rica", "CRCAL")]
    [DataRow("Puerto Moín, Costa Rica", "CRMOIN")]
    [DataRow("Cartagena, Colombia", "COCTG")]
    public void NonPanamaFcl_DoesNotInheritAnotherOptionsAllIn(string poeName, string poeCode)
    {
        Assert.IsFalse(PanamaCostaRicaAllInPolicy.Applies(
            "Fcl", poeCode, poeName, "CRSJO", "San José, Costa Rica"));
    }

    [DataTestMethod]
    [DataRow("Lcl", "San José, Costa Rica")]
    [DataRow("Ftl", "San José, Costa Rica")]
    [DataRow("Fcl", "Balboa, Panamá")]
    [DataRow("Fcl", "Cartagena, Colombia")]
    public void OtherModesOrDestinations_DoNotUseAllIn(string mode, string destination)
    {
        Assert.IsFalse(PanamaCostaRicaAllInPolicy.Applies(
            mode, "PABLB", "Balboa, Panamá", null, destination));
    }
}
