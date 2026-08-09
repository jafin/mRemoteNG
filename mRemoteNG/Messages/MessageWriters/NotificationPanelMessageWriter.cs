using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Windows.Forms;
using mRemoteNG.UI;
using mRemoteNG.UI.Window;

namespace mRemoteNG.Messages.MessageWriters
{
    [SupportedOSPlatform("windows")]
    public class NotificationPanelMessageWriter(ErrorAndInfoWindow messageWindow) : IMessageWriter
    {
        private readonly ErrorAndInfoWindow _messageWindow = messageWindow ?? throw new ArgumentNullException(nameof(messageWindow));
        private List<ListViewItem>? _pendingItems = [];

        /// <summary>
        /// Set once the deferred flush has been posted. Until it runs, messages keep buffering:
        /// letting one through early would put it in the list ahead of older ones and reintroduce
        /// the missing-column problem the deferral exists to avoid.
        /// </summary>
        private bool _flushPosted;

        public void Write(IMessage message)
        {
            NotificationMessageListViewItem lvItem = new(message);
            AddToList(lvItem);
        }

        private void AddToList(ListViewItem lvItem)
        {
            if (_messageWindow.lvErrorCollector.IsDisposed)
                return;

            // Buffer messages until the control handle is created.
            // ErrorAndInfoWindow starts in DockBottomAutoHide — its handle is only
            // created when the user first opens the panel, which is well after
            // startup timing messages are posted (#53).
            if (_pendingItems != null)
            {
                if (_flushPosted || !_messageWindow.lvErrorCollector.IsHandleCreated)
                {
                    if (_pendingItems.Count == 0 && !_flushPosted)
                        _messageWindow.lvErrorCollector.HandleCreated += OnHandleCreated;
                    _pendingItems.Add(lvItem);
                    return;
                }

                // Handle already exists — flush and switch to direct mode
                FlushPending();
            }

            if (_messageWindow.lvErrorCollector.InvokeRequired)
            {
                try
                {
                    _messageWindow.lvErrorCollector.Invoke((MethodInvoker)(() => AddToList(lvItem)));
                }
                catch (System.ComponentModel.InvalidAsynchronousStateException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (InvalidOperationException)
                {
                    return;
                }
            }
            else
            {
                _messageWindow.AddMessage(lvItem);
            }
        }

        private void OnHandleCreated(object? sender, EventArgs e)
        {
            _messageWindow.lvErrorCollector.HandleCreated -= OnHandleCreated;

            // Post the flush rather than running it here. ListView.OnHandleCreated raises this
            // event before it pushes its Columns to the native control, so an item added from
            // inside the handler is inserted into a control that has no second column yet and
            // renders its timestamp only — its message text never reaches the native item. A
            // later Items.Clear() and re-add repairs it, which is why searching used to make the
            // text appear. Posting puts the flush after the rest of handle creation.
            try
            {
                _flushPosted = true;
                _messageWindow.lvErrorCollector.BeginInvoke((MethodInvoker)FlushPending);
            }
            catch (InvalidOperationException)
            {
                // Covers ObjectDisposedException too: the handle went away again between the event
                // and this call. The items stay buffered and are dropped with the window.
            }
        }

        private void FlushPending()
        {
            if (_pendingItems == null) return;
            var items = _pendingItems;
            _pendingItems = null; // switch to direct mode permanently
            _flushPosted = false;

            // Oldest first: AddMessage inserts at the top, so replaying in collection order leaves
            // the newest message at the top, matching live delivery.
            foreach (var pending in items)
                _messageWindow.AddMessage(pending);
        }
    }
}
