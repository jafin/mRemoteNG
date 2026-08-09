using System;
using System.Globalization;
using System.Windows.Forms;
using mRemoteNG.Messages;

namespace mRemoteNG.UI
{
    public class NotificationMessageListViewItem : ListViewItem
    {
        private readonly string _messageText;

        public NotificationMessageListViewItem(IMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);

            // Newlines are collapsed so a multi-line message stays one row; the full text is in the
            // detail pane and in the clipboard output.
            _messageText = message.Text.Replace(Environment.NewLine, "  ");

            ImageIndex = Convert.ToInt32(message.Class, CultureInfo.InvariantCulture);

            // The time the message was recorded, not the time it is rendered. Messages collected
            // before the writers were attached are delivered later, and stamping them on render
            // would report the moment of delivery instead of the moment of the event.
            Text = message.Date.ToString("T", CultureInfo.CurrentCulture);

            SubItems.Add(_messageText);

            Tag = message;
        }

        /// <summary>
        /// The message text as displayed, which is what the panel's search box filters on. The
        /// timestamp deliberately does not take part — searching "10" should not match every message
        /// logged in the tenth minute of an hour.
        /// </summary>
        public string MessageText => _messageText;

        /// <summary>
        /// Re-applies the message text now that the item belongs to a list view.
        /// </summary>
        /// <remarks>
        /// Subitem text assigned before an item is attached only reaches the native control if that
        /// control already has the column to carry it. An item added while the list view is still
        /// creating its handle — columns are pushed after the <c>HandleCreated</c> event — therefore
        /// shows its timestamp and nothing else. Re-assigning after insertion pushes the text
        /// through, at the cost of one native call per message.
        /// </remarks>
        public void RealizeMessageText()
        {
            if (SubItems.Count > 1)
                SubItems[1].Text = _messageText;
        }
    }
}
