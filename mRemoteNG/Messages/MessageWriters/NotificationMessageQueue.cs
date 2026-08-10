using System.Collections.Generic;
using System.Threading;

namespace mRemoteNG.Messages.MessageWriters;

/// <summary>
/// The single ordering point between the threads that report messages and the UI thread that
/// renders them.
/// </summary>
/// <remarks>
/// <para>
/// Messages arrive from background workers and from the UI thread alike. Delivering the UI
/// thread's own messages straight to the panel while a background message was still on its way
/// there put them in the panel in the wrong order, because the panel inserts each entry at the
/// top. Everything goes through here instead, so panel order is enqueue order.
/// </para>
/// <para>
/// It also holds the messages that arrive before the panel has a window to render into. The
/// panel starts auto-hidden and its handle is created only when the user first opens it, which
/// is well after the startup messages worth reading have been reported.
/// </para>
/// </remarks>
internal sealed class NotificationMessageQueue
{
    private readonly Lock _lock = new();
    private readonly Queue<IMessage> _queue = new();
    private bool _drainScheduled;

    /// <summary>
    /// Queues a message.
    /// </summary>
    /// <returns>
    /// True when the caller must schedule a drain. False when one is already scheduled and will
    /// take this message with it — the caller must not schedule a second, or two drains race to
    /// dequeue and the panel receives its entries interleaved.
    /// </returns>
    public bool Enqueue(IMessage message)
    {
        lock (_lock)
        {
            _queue.Enqueue(message);

            if (_drainScheduled)
                return false;

            _drainScheduled = true;
            return true;
        }
    }

    /// <summary>
    /// Takes the next message to render, oldest first.
    /// </summary>
    /// <remarks>
    /// Returning null also clears the scheduled flag, so emptying the queue and giving up the
    /// drain are one step. Split in two, a message enqueued between them would sit in the queue
    /// with nothing scheduled to collect it.
    /// </remarks>
    public IMessage? Dequeue()
    {
        lock (_lock)
        {
            if (_queue.Count == 0)
            {
                _drainScheduled = false;
                return null;
            }

            return _queue.Dequeue();
        }
    }

    /// <summary>
    /// Gives up a drain that could not be scheduled, so that the next message queued schedules
    /// one instead of assuming a drain is already on its way.
    /// </summary>
    public void ReleaseDrain()
    {
        lock (_lock)
            _drainScheduled = false;
    }

    /// <summary>
    /// Discards everything queued, for when there is no longer a panel to render into.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _queue.Clear();
            _drainScheduled = false;
        }
    }

    /// <summary>
    /// The number of messages waiting. For tests and diagnostics.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
                return _queue.Count;
        }
    }
}
