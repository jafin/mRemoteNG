using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using mRemoteNG.Messages;
using mRemoteNG.Messages.MessageWriters;
using NUnit.Framework;

namespace mRemoteNGTests.Messages;

/// <summary>
/// Covers "panel entries appear in the order they were reported" and the thread safety of the
/// buffer behind it, in <c>specs/notification-panel/spec.md</c>.
/// </summary>
[TestFixture]
public class NotificationMessageQueueTests
{
    private static readonly string[] ThreeInOrder = ["first", "second", "third"];
    private static readonly string[] BothAfterAReleasedDrain = ["one", "two"];

    private NotificationMessageQueue _queue = null!;

    [SetUp]
    public void Setup() => _queue = new NotificationMessageQueue();

    private static Message Msg(string text) => new(MessageClass.InformationMsg, text);

    private List<string> DrainAll()
    {
        List<string> drained = [];
        while (_queue.Dequeue() is { } message)
            drained.Add(message.Text);
        return drained;
    }

    #region Ordering

    [Test]
    public void MessagesComeOutInTheOrderTheyWentIn()
    {
        // Oldest first: the panel inserts each entry at the top, so this is what leaves the
        // newest message showing at the top.
        _queue.Enqueue(Msg("first"));
        _queue.Enqueue(Msg("second"));
        _queue.Enqueue(Msg("third"));

        Assert.That(DrainAll(), Is.EqualTo(ThreeInOrder));
    }

    [Test]
    public void AMessageQueuedDuringADrainKeepsItsPlaceAtTheBack()
    {
        // This is the interleaving that motivated the queue: a message reported while an
        // earlier one is on its way to the panel must not overtake it.
        _queue.Enqueue(Msg("background"));

        Assert.That(_queue.Dequeue()!.Text, Is.EqualTo("background"));

        _queue.Enqueue(Msg("from the ui thread"));

        Assert.That(_queue.Dequeue()!.Text, Is.EqualTo("from the ui thread"));
    }

    #endregion

    #region Scheduling

    [Test]
    public void TheFirstMessageAsksForADrain()
    {
        Assert.That(_queue.Enqueue(Msg("one")), Is.True);
    }

    [Test]
    public void FurtherMessagesDoNotAskForASecondDrain()
    {
        _queue.Enqueue(Msg("one"));

        Assert.That(_queue.Enqueue(Msg("two")), Is.False,
            "two drains racing to dequeue would deliver the panel its entries interleaved");
        Assert.That(_queue.Enqueue(Msg("three")), Is.False);
    }

    [Test]
    public void EmptyingTheQueueGivesUpTheDrain()
    {
        _queue.Enqueue(Msg("one"));
        DrainAll();

        Assert.That(_queue.Enqueue(Msg("two")), Is.True,
            "nothing is scheduled once the queue has drained, so the next message must schedule one");
    }

    [Test]
    public void AMessageQueuedBeforeTheDrainEndsIsCollectedByIt()
    {
        _queue.Enqueue(Msg("one"));

        // Drain takes "one" but has not yet found the queue empty.
        Assert.That(_queue.Dequeue()!.Text, Is.EqualTo("one"));

        // So this must not schedule a second drain — the running one will collect it.
        Assert.That(_queue.Enqueue(Msg("two")), Is.False);
        Assert.That(_queue.Dequeue()!.Text, Is.EqualTo("two"));
    }

    [Test]
    public void GivingUpADrainLetsTheNextMessageScheduleOne()
    {
        // What happens when the panel's handle disappears between scheduling and posting: the
        // messages stay queued, and the next one has to schedule a fresh drain or they are
        // never rendered.
        _queue.Enqueue(Msg("one"));
        _queue.ReleaseDrain();

        Assert.That(_queue.Enqueue(Msg("two")), Is.True);
        Assert.That(DrainAll(), Is.EqualTo(BothAfterAReleasedDrain));
    }

    [Test]
    public void ClearingDiscardsEverythingAndTheDrainWithIt()
    {
        _queue.Enqueue(Msg("one"));
        _queue.Enqueue(Msg("two"));

        _queue.Clear();

        Assert.That(_queue.Count, Is.Zero);
        Assert.That(_queue.Enqueue(Msg("three")), Is.True);
    }

    #endregion

    #region Concurrency

    [Test]
    public void ConcurrentReportersLoseNothing()
    {
        // The buffer this replaces was a bare List mutated by whichever thread reported the
        // message, with no synchronization at all.
        const int threads = 8;
        const int perThread = 500;

        Parallel.For(0, threads, t =>
        {
            for (int i = 0; i < perThread; i++)
                _queue.Enqueue(Msg($"{t}:{i}"));
        });

        Assert.That(_queue.Count, Is.EqualTo(threads * perThread));
        Assert.That(DrainAll(), Has.Count.EqualTo(threads * perThread));
    }

    [Test]
    public void ExactlyOneConcurrentReporterIsToldToScheduleTheDrain()
    {
        // Two threads both being told to schedule would put two drains on the UI thread,
        // dequeuing against each other.
        const int threads = 16;
        int scheduleRequests = 0;

        Parallel.For(0, threads, _ =>
        {
            if (_queue.Enqueue(Msg("concurrent")))
                System.Threading.Interlocked.Increment(ref scheduleRequests);
        });

        Assert.That(scheduleRequests, Is.EqualTo(1));
    }

    [Test]
    public void EveryMessageIsDeliveredExactlyOnceUnderConcurrentDraining()
    {
        const int total = 2000;
        for (int i = 0; i < total; i++)
            _queue.Enqueue(Msg(i.ToString()));

        // Dequeue is safe to call from anywhere even though only the UI thread does; nothing
        // may be handed out twice or dropped.
        System.Collections.Concurrent.ConcurrentBag<string> drained = [];
        Parallel.For(0, 4, _ =>
        {
            while (_queue.Dequeue() is { } message)
                drained.Add(message.Text);
        });

        Assert.That(drained, Has.Count.EqualTo(total));
        Assert.That(drained.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(total));
    }

    #endregion

    [Test]
    public void AnEmptyQueueHandsBackNothing()
    {
        Assert.That(_queue.Dequeue(), Is.Null);
        Assert.That(_queue.Count, Is.Zero);
    }
}
