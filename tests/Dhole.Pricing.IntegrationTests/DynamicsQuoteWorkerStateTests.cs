using Dhole.Pricing.Api.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class DynamicsQuoteWorkerStateTests
{
    [TestMethod]
    public void OnlyExactAdminRecoveryMarkerCanAuthorizeSingleLineAdoption()
    {
        var rateId = Guid.Parse("7f730a45-386e-4f12-be40-9db110736d36");
        var json = System.Text.Json.JsonSerializer.Serialize(new {
            adoptSingleUnmanagedLine = true,
            rateId = rateId.ToString("D"),
            revisionNumber = 3
        });
        Assert.IsTrue(DynamicsQuoteDeliveryWorker.IsAuthorizedSingleLineAdoption(json, rateId, 3));
        Assert.IsFalse(DynamicsQuoteDeliveryWorker.IsAuthorizedSingleLineAdoption(json, rateId, 4));
        Assert.IsFalse(DynamicsQuoteDeliveryWorker.IsAuthorizedSingleLineAdoption(json, Guid.NewGuid(), 3));
        Assert.IsFalse(DynamicsQuoteDeliveryWorker.IsAuthorizedSingleLineAdoption("{}", rateId, 3));
        Assert.IsFalse(DynamicsQuoteDeliveryWorker.IsAuthorizedSingleLineAdoption("{invalid", rateId, 3));
        Assert.IsFalse(DynamicsQuoteDeliveryWorker.IsAuthorizedSingleLineAdoption(null, rateId, 3));
    }

    [TestMethod]
    public void StartsUnreadyAndNeverAssumesTheWorkerIsRunning()
    {
        var state = new DynamicsQuoteWorkerState();
        var snapshot = state.Snapshot();
        Assert.IsFalse(snapshot.Ready);
        Assert.AreEqual("DYNAMICS_WORKER_NOT_STARTED", snapshot.Code);
        Assert.IsNull(snapshot.LastHeartbeatUtc);
    }

    [TestMethod]
    public void PreservesTheBlockReasonAndResumesOnlyAfterReadyProbe()
    {
        var state = new DynamicsQuoteWorkerState();
        state.Report(false, "quote_grants_incomplete");
        Assert.IsFalse(state.Snapshot().Ready);
        Assert.AreEqual("quote_grants_incomplete", state.Snapshot().Code);

        state.Heartbeat();
        state.Report(true, "DYNAMICS_QUOTE_WRITES_READY");
        Assert.IsTrue(state.Snapshot().Ready);
        Assert.AreEqual("DYNAMICS_QUOTE_WRITES_READY", state.Snapshot().Code);
        Assert.IsNotNull(state.Snapshot().LastHeartbeatUtc);
    }

    [TestMethod]
    public void MissingKeyIsReportedWithoutMarkingJobsAsAttempted()
    {
        var state = new DynamicsQuoteWorkerState();
        state.Report(false, "DYNAMICS_API_KEY_MISSING");
        Assert.IsFalse(state.Snapshot().Ready);
        Assert.AreEqual("DYNAMICS_API_KEY_MISSING", state.Snapshot().Code);
    }
}
