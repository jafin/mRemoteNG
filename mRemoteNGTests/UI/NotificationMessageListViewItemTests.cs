using System;
using System.Globalization;
using mRemoteNG.Messages;
using mRemoteNG.UI;
using NUnit.Framework;

namespace mRemoteNGTests.UI;

/// <summary>
/// Covers "panel entries show when they were reported" in
/// <c>specs/notification-panel/spec.md</c>.
/// </summary>
[TestFixture]
public class NotificationMessageListViewItemTests
{
    [Test]
    public void TheEntryShowsTheMessagesOwnTimestamp()
    {
        Message message = new(MessageClass.InformationMsg, "hello")
        {
            Date = new DateTime(2026, 8, 8, 14, 23, 5, DateTimeKind.Local)
        };

        NotificationMessageListViewItem item = new(message);

        Assert.That(item.Text,
            Is.EqualTo(message.Date.ToString("T", CultureInfo.CurrentCulture)));
    }

    [Test]
    public void AReplayedMessageKeepsItsOriginalTime()
    {
        // Messages collected before the writers were attached are rendered later. Stamping on
        // render would report delivery time rather than when the event happened.
        DateTime whenItHappened = DateTime.Now.AddMinutes(-42);
        Message message = new(MessageClass.ErrorMsg, "startup failure") { Date = whenItHappened };

        NotificationMessageListViewItem item = new(message);

        Assert.That(item.Text, Is.EqualTo(whenItHappened.ToString("T", CultureInfo.CurrentCulture)));
    }

    [Test]
    public void TheMessageTextIsCarriedSeparatelyFromTheTimestamp()
    {
        NotificationMessageListViewItem item = new(new Message(MessageClass.InformationMsg, "the text"));

        Assert.That(item.MessageText, Is.EqualTo("the text"));
    }

    [Test]
    public void NewlinesAreCollapsedSoAMessageStaysOneRow()
    {
        Message message = new(MessageClass.InformationMsg, "line one" + Environment.NewLine + "line two");

        NotificationMessageListViewItem item = new(message);

        Assert.Multiple(() =>
        {
            Assert.That(item.MessageText, Is.EqualTo("line one  line two"));
            Assert.That(item.MessageText, Does.Not.Contain(Environment.NewLine));
        });
    }

    [Test]
    public void TheMessageIsKeptOnTheItemForTheDetailPane()
    {
        Message message = new(MessageClass.WarningMsg, "warn");

        NotificationMessageListViewItem item = new(message);

        Assert.That(item.Tag, Is.SameAs(message));
    }

    [Test]
    public void TheIconMatchesTheMessageClass()
    {
        NotificationMessageListViewItem item = new(new Message(MessageClass.ErrorMsg, "boom"));

        Assert.That(item.ImageIndex, Is.EqualTo((int)MessageClass.ErrorMsg));
    }

    [Test]
    public void ANullMessageIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new NotificationMessageListViewItem(null!));
    }
}