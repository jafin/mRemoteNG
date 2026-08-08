using System;
using System.Globalization;
using System.Windows.Forms;
using mRemoteNG.Messages;

namespace mRemoteNG.UI
{
    public class NotificationMessageListViewItem : ListViewItem
    {
        public NotificationMessageListViewItem(IMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);

            ImageIndex = Convert.ToInt32(message.Class, CultureInfo.InvariantCulture);

            // The time the message was recorded, not the time it is rendered. Messages collected
            // before the writers were attached are delivered later, and stamping them on render
            // would report the moment of delivery instead of the moment of the event.
            Text = message.Date.ToString("T", CultureInfo.CurrentCulture);

            // Newlines are collapsed so a multi-line message stays one row; the full text is in the
            // detail pane and in the clipboard output.
            SubItems.Add(message.Text.Replace(Environment.NewLine, "  "));

            Tag = message;
        }

        /// <summary>
        /// The message text as displayed, which is what the panel's search box filters on. The
        /// timestamp deliberately does not take part — searching "10" should not match every message
        /// logged in the tenth minute of an hour.
        /// </summary>
        public string MessageText => SubItems.Count > 1 ? SubItems[1].Text : string.Empty;
    }
}
