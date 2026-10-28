namespace Dhole.Pricing.Api.Services;

/// <summary>
/// In-process, read-only heartbeat for the Dynamics quote delivery hosted service.
/// This reports why a queue is not draining without mutating jobs or Dataverse.
/// </summary>
public sealed class DynamicsQuoteWorkerState
{
    private readonly object _sync = new();
    private bool _ready;
    private string _code = "DYNAMICS_WORKER_NOT_STARTED";
    private DateTime? _lastHeartbeatUtc;

    public void Report(bool ready, string code)
    {
        lock (_sync)
        {
            _ready = ready;
            _code = code;
            _lastHeartbeatUtc = DateTime.UtcNow;
        }
    }

    public void Heartbeat()
    {
        lock (_sync)
            _lastHeartbeatUtc = DateTime.UtcNow;
    }

    public DynamicsQuoteWorkerSnapshot Snapshot()
    {
        lock (_sync)
        {
            // A stale heartbeat is different from valid connector denial.
            // Never claim that a stalled worker is ready to send quotes.
            if (_lastHeartbeatUtc is DateTime last &&
                DateTime.UtcNow - last > TimeSpan.FromMinutes(2) &&
                _code is not ("DYNAMICS_SYNC_DISABLED" or "DYNAMICS_API_KEY_MISSING"))
                return new(false, "DYNAMICS_WORKER_UNRESPONSIVE", last);

            return new(_ready, _code, _lastHeartbeatUtc);
        }
    }
}

public sealed record DynamicsQuoteWorkerSnapshot(
    bool Ready, string Code, DateTime? LastHeartbeatUtc);
