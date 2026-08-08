using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;

// ReSharper disable ArrangeAccessorOwnerBody

namespace mRemoteNG.Messages
{
    [SupportedOSPlatform("windows")]
    public class MessageCollector : INotifyCollectionChanged
    {
        private const int MaxMessages = 10_000;
        private readonly IList<IMessage> _messageList;
        private readonly object _listLock = new();

        /// <summary>
        /// The messages collected so far, as a snapshot.
        /// </summary>
        /// <remarks>
        /// A copy rather than the live list: messages arrive from background workers as well as the
        /// UI thread, so handing out the backing list would let any caller enumerate it while it is
        /// being appended to.
        /// </remarks>
        public IEnumerable<IMessage> Messages
        {
            get
            {
                lock (_listLock)
                    return _messageList.ToArray();
            }
        }

        public MessageCollector()
        {
            _messageList = new List<IMessage>();
        }

        public void AddMessage(MessageClass messageClass, string messageText, bool onlyLog = false)
        {
            Message message = new(messageClass, messageText, onlyLog);
            AddMessage(message);
        }

        public void AddMessage(IMessage message)
        {
            AddMessages(new[] {message});
        }

        public void AddMessages(IEnumerable<IMessage> messages)
        {
            List<IMessage> newMessages = new();

            // Messages arrive from background workers (e.g. the port scanner's scan threads) as well as
            // the UI thread, so the backing list must not be mutated concurrently.
            lock (_listLock)
            {
                foreach (IMessage message in messages)
                {
                    _messageList.Add(message);
                    newMessages.Add(message);
                }

                // Prevent unbounded growth in long-running sessions. Trim in one shot: removing from
                // the front one item at a time shifts the whole list on every message once the cap is
                // reached, which is a large cost under a flood of messages.
                int excess = _messageList.Count - MaxMessages;
                if (excess > 0 && _messageList is List<IMessage> backingList)
                    backingList.RemoveRange(0, excess);
                else
                    while (_messageList.Count > MaxMessages)
                        _messageList.RemoveAt(0);
            }

            if (newMessages.Count > 0)
                RaiseCollectionChangedEvent(NotifyCollectionChangedAction.Add, newMessages);
        }

        public void AddExceptionMessage(string message, Exception ex, MessageClass msgClass = MessageClass.ErrorMsg, bool logOnly = true)
        {
            AddMessage(msgClass, message + Environment.NewLine + Tools.MiscTools.GetExceptionMessageRecursive(ex),
                       logOnly);
        }

        public void AddExceptionStackTrace(string message, Exception ex, MessageClass msgClass = MessageClass.ErrorMsg, bool logOnly = true)
        {
            AddMessage(msgClass, message + Environment.NewLine + ex.Message + Environment.NewLine + ex.Demystify().StackTrace,
                       logOnly);
        }

        public void ClearMessages()
        {
            lock (_listLock)
                _messageList.Clear();
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> and immediately hands it everything collected so
        /// far, so a handler attached late still sees the messages it missed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This exists because the collector is a static singleton that starts receiving messages
        /// long before the message writers are attached during <c>FrmMain_Load</c>. Without it,
        /// anything reported in between reaches no writer at all — not the panel, not the popup
        /// writer, not the log file — including the errors raised when loading or upgrading settings
        /// fails.
        /// </para>
        /// <para>
        /// Subscribing and snapshotting happen under the same lock, so no message can be appended
        /// between the two and be missed. The backlog is then delivered outside the lock: writers
        /// marshal to the UI thread, and holding the lock across that would deadlock against a
        /// background thread already waiting to add a message.
        /// </para>
        /// <para>
        /// Delivering outside the lock leaves a theoretical window where a message appended just
        /// before subscription is delivered twice — once from the backlog, once by its own event,
        /// which is raised outside the lock too. That window requires a concurrent writer, and the
        /// only intended caller runs during form load before any background work has started. This
        /// is a startup helper, not a general-purpose subscribe.
        /// </para>
        /// </remarks>
        public void SubscribeAndReplay(NotifyCollectionChangedEventHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            IMessage[] backlog;

            lock (_listLock)
            {
                backlog = _messageList.ToArray();
                CollectionChanged += handler;
            }

            if (backlog.Length > 0)
                handler(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, backlog));
        }

        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        private void RaiseCollectionChangedEvent(NotifyCollectionChangedAction action, IList items)
        {
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(action, items));
        }
    }
}