using System;
using System.Runtime.Versioning;
using System.Windows.Forms;
using mRemoteNG.UI;
using mRemoteNG.UI.Window;

namespace mRemoteNG.Messages.MessageWriters;

[SupportedOSPlatform("windows")]
public class NotificationPanelMessageWriter(ErrorAndInfoWindow messageWindow) : IMessageWriter
{
    private readonly ErrorAndInfoWindow _messageWindow = messageWindow ?? throw new ArgumentNullException(nameof(messageWindow));
    private readonly NotificationMessageQueue _queue = new();

    /// <summary>
    /// Queues the message and makes sure a drain is on its way to the UI thread.
    /// </summary>
    /// <remarks>
    /// Called from whichever thread reported the message. Nothing here touches the panel or any
    /// state that is not synchronized, so there is no thread this is unsafe to call from — which
    /// was not true when the buffering was a bare list mutated in place by every caller.
    /// </remarks>
    public void Write(IMessage message)
    {
        if (_messageWindow.lvErrorCollector.IsDisposed)
            return;

        if (_queue.Enqueue(message))
            ScheduleDrain();
    }

    private void ScheduleDrain()
    {
        ListView list = _messageWindow.lvErrorCollector;

        // No window to post to yet. The panel starts in DockBottomAutoHide and its handle is
        // created only when the user first opens it, which is well after startup messages are
        // reported (#53). The messages stay queued until then.
        if (!list.IsHandleCreated)
        {
            list.HandleCreated += OnHandleCreated;

            // The handle may have appeared between the test and the subscription, in which case
            // nothing will raise the event and the queue would never be drained.
            if (!list.IsHandleCreated)
                return;

            list.HandleCreated -= OnHandleCreated;
        }

        PostDrain(list);
    }

    private void OnHandleCreated(object? sender, EventArgs e)
    {
        ListView list = _messageWindow.lvErrorCollector;
        list.HandleCreated -= OnHandleCreated;

        // Post rather than drain here. ListView.OnHandleCreated raises this event before it
        // pushes its Columns to the native control, so an item added from inside the handler is
        // inserted into a control with no second column yet and renders its timestamp only.
        PostDrain(list);
    }

    private void PostDrain(ListView list)
    {
        try
        {
            list.BeginInvoke((MethodInvoker)Drain);
        }
        catch (InvalidOperationException)
        {
            // Covers ObjectDisposedException too: the handle went away between the check and
            // this call. Give up the drain so the next message queued schedules a fresh one.
            _queue.ReleaseDrain();
        }
    }

    /// <summary>
    /// Renders everything queued. Runs on the UI thread.
    /// </summary>
    /// <remarks>
    /// Oldest first, because the panel inserts each entry at the top, so draining in enqueue
    /// order leaves the newest message at the top and matches what the timestamps say.
    /// </remarks>
    private void Drain()
    {
        while (true)
        {
            if (_messageWindow.lvErrorCollector.IsDisposed)
            {
                _queue.Clear();
                return;
            }

            IMessage? message = _queue.Dequeue();
            if (message is null)
                return;

            _messageWindow.AddMessage(new NotificationMessageListViewItem(message));
        }
    }
}
