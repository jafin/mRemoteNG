using System.Threading;

namespace mRemoteNG.FileTransfer;

/// <summary>
/// Lets one operation run at a time, turning away the rest rather than queueing them.
/// </summary>
/// <remarks>
/// Turning away is the point, and is what separates this from a lock. A caller that waits would
/// run its attempt once the first had already succeeded — reconnecting a session that is by then
/// healthy, or authenticating a second time for nothing. "Somebody is already doing this" is the
/// correct answer, not a reason to wait.
/// </remarks>
public sealed class SingleFlight
{
    private readonly Lock _gate = new();
    private bool _running;

    /// <summary>Whether an operation is in flight.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _running;
        }
    }

    /// <summary>Claims the slot, or reports that somebody else holds it.</summary>
    public bool TryEnter()
    {
        lock (_gate)
        {
            if (_running)
                return false;

            _running = true;
            return true;
        }
    }

    /// <summary>Releases the slot. Safe to call when it was never claimed.</summary>
    public void Exit()
    {
        lock (_gate)
            _running = false;
    }
}