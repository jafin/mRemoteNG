using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.Messages;
using NUnit.Framework;

namespace mRemoteNGTests.Messages;

/// <summary>
/// Covers "no message is lost to writer registration order" in
/// <c>specs/notification-panel/spec.md</c>.
/// </summary>
[TestFixture]
public class MessageCollectorBacklogTests
{
    private static readonly string[] BeforeOneAndTwo = ["before one", "before two"];
    private static readonly string[] BeforeThenAfter = ["before", "after"];
    private static readonly string[] LiveOnly = ["live"];

    private MessageCollector _collector = null!;
    private List<IMessage> _delivered = null!;

    [SetUp]
    public void Setup()
    {
        _collector = new MessageCollector();
        _delivered = [];
    }

    private NotifyCollectionChangedEventHandler Recorder =>
        (_, args) =>
        {
            if (args.NewItems is null) return;
            _delivered.AddRange(args.NewItems.Cast<IMessage>());
        };

    private static Message Msg(string text) => new(MessageClass.InformationMsg, text);

    [Test]
    public void MessagesAddedBeforeSubscribingAreStillDelivered()
    {
        // The startup defect: writers are attached in FrmMain_Load, well after settings loading
        // has already reported. Without replay those messages reach no writer at all.
        _collector.AddMessage(Msg("before one"));
        _collector.AddMessage(Msg("before two"));

        _collector.SubscribeAndReplay(Recorder);

        Assert.That(_delivered.Select(m => m.Text), Is.EqualTo(BeforeOneAndTwo));
    }

    [Test]
    public void TheBacklogIsDeliveredInTheOrderItWasCollected()
    {
        foreach (int i in Enumerable.Range(0, 20))
            _collector.AddMessage(Msg($"m{i}"));

        _collector.SubscribeAndReplay(Recorder);

        Assert.That(_delivered.Select(m => m.Text),
            Is.EqualTo(Enumerable.Range(0, 20).Select(i => $"m{i}")));
    }

    [Test]
    public void EachMessageIsDeliveredExactlyOnce()
    {
        _collector.AddMessage(Msg("before"));

        _collector.SubscribeAndReplay(Recorder);
        _collector.AddMessage(Msg("after"));

        Assert.That(_delivered.Select(m => m.Text), Is.EqualTo(BeforeThenAfter));
    }

    [Test]
    public void SubscribingToAnEmptyCollectorDeliversNothing()
    {
        _collector.SubscribeAndReplay(Recorder);

        Assert.That(_delivered, Is.Empty);
    }

    [Test]
    public void MessagesAddedAfterSubscribingAreDeliveredLive()
    {
        _collector.SubscribeAndReplay(Recorder);

        _collector.AddMessage(Msg("live"));

        Assert.That(_delivered.Select(m => m.Text), Is.EqualTo(LiveOnly));
    }

    [Test]
    public void AStartupFailureReportedBeforeSubscribingIsNotLost()
    {
        // The concrete case: SettingsLoader reports "Loading settings failed" as an error before
        // any writer exists. That error used to reach the panel, the popup writer and the log
        // file alike -- which is to say, nothing at all.
        _collector.AddExceptionMessage("Loading settings failed", new InvalidOperationException("disk on fire"));

        _collector.SubscribeAndReplay(Recorder);

        Assert.Multiple(() =>
        {
            Assert.That(_delivered, Has.Count.EqualTo(1));
            Assert.That(_delivered[0].Class, Is.EqualTo(MessageClass.ErrorMsg));
            Assert.That(_delivered[0].Text, Does.Contain("Loading settings failed"));
            Assert.That(_delivered[0].Text, Does.Contain("disk on fire"));
        });
    }

    [Test]
    public void MultipleSubscribersEachReceiveTheBacklog()
    {
        _collector.AddMessage(Msg("shared"));
        List<IMessage> second = [];

        _collector.SubscribeAndReplay(Recorder);
        _collector.SubscribeAndReplay((_, args) =>
        {
            if (args.NewItems is not null) second.AddRange(args.NewItems.Cast<IMessage>());
        });

        Assert.Multiple(() =>
        {
            Assert.That(_delivered, Has.Count.EqualTo(1));
            Assert.That(second, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ANullHandlerIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => _collector.SubscribeAndReplay(null!));
    }

    // ---- snapshot semantics -------------------------------------------------------

    [Test]
    public void ReadingTheMessagesWhileAnotherThreadAddsDoesNotThrow()
    {
        using CancellationTokenSource cts = new();

        Task writer = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
                _collector.AddMessage(Msg("churn"));
        });

        try
        {
            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < 500; i++)
                {
                    // Enumerating the live backing list would race the writer above and throw
                    // "Collection was modified".
                    _ = _collector.Messages.Count();
                }
            });
        }
        finally
        {
            cts.Cancel();
            writer.Wait(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public void TheReturnedMessagesAreASnapshot()
    {
        _collector.AddMessage(Msg("first"));
        IEnumerable<IMessage> snapshot = _collector.Messages;

        _collector.AddMessage(Msg("second"));

        Assert.That(snapshot.Count(), Is.EqualTo(1));
    }
}