using Dhole.Pricing.Api.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class DynamicsQuoteWorkerStateTests
{
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
