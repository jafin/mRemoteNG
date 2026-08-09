using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.FileTransfer;
using NUnit.Framework;

namespace mRemoteNGTests.FileTransfer
{
    /// <summary>
    /// Covers "transfers are managed through a queue" in <c>specs/sftp-browser-panel/spec.md</c>.
    /// The transfer operation is a delegate, so none of this needs a server.
    /// </summary>
    [TestFixture]
    public class TransferQueueTests
    {
        private static readonly string[] AbcOrder = [@"C:\local\a", @"C:\local\b", @"C:\local\c"];

        private static TransferItem Item(string name = "file.txt", long size = 100) =>
            new(TransferDirection.Upload, $@"C:\local\{name}", $"/remote/{name}", size);

        private static async Task Idle(TransferQueue queue) =>
            await queue.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));

        // ---- running -----------------------------------------------------------------

        [Test]
        public async Task AQueuedItemIsRunAndSucceeds()
        {
            using TransferQueue queue = new((_, _, _) => Task.CompletedTask);
            TransferItem item = Item();

            queue.Enqueue(item);
            await Idle(queue);

            Assert.That(item.Status, Is.EqualTo(TransferStatus.Succeeded));
        }

        [Test]
        public async Task ItemsRunInTheOrderTheyWereQueued()
        {
            List<string> order = [];
            using TransferQueue queue = new((item, _, _) =>
            {
                lock (order) order.Add(item.SourcePath);
                return Task.CompletedTask;
            });

            queue.Enqueue(Item("a"));
            queue.Enqueue(Item("b"));
            queue.Enqueue(Item("c"));
            await Idle(queue);

            Assert.That(order, Is.EqualTo(AbcOrder));
        }

        [Test]
        public async Task OnlyOneItemRunsAtATime()
        {
            // Concurrent transfers over one SFTP channel contend for the same window and usually
            // make the whole set slower.
            int running = 0;
            int peak = 0;

            using TransferQueue queue = new(async (_, _, ct) =>
            {
                int now = Interlocked.Increment(ref running);
                InterlockedMax(ref peak, now);
                await Task.Delay(20, ct);
                Interlocked.Decrement(ref running);
            });

            queue.EnqueueRange([Item("a"), Item("b"), Item("c")]);
            await Idle(queue);

            Assert.That(peak, Is.EqualTo(1));
        }

        [Test]
        public async Task TheQueueDrainsAndStops()
        {
            using TransferQueue queue = new((_, _, _) => Task.CompletedTask);

            queue.Enqueue(Item());
            await Idle(queue);

            Assert.That(queue.IsRunning, Is.False);
        }

        [Test]
        public async Task QueueingAgainAfterDrainingStartsTheRunnerBackUp()
        {
            using TransferQueue queue = new((_, _, _) => Task.CompletedTask);
            queue.Enqueue(Item("first"));
            await Idle(queue);

            TransferItem second = Item("second");
            queue.Enqueue(second);
            await Idle(queue);

            Assert.That(second.Status, Is.EqualTo(TransferStatus.Succeeded));
        }

        // ---- failure -------------------------------------------------------------------

        [Test]
        public async Task AFailedItemRecordsTheReason()
        {
            using TransferQueue queue = new((_, _, _) => throw new UnauthorizedAccessException("permission denied"));
            TransferItem item = Item();

            queue.Enqueue(item);
            await Idle(queue);

            Assert.Multiple(() =>
            {
                Assert.That(item.Status, Is.EqualTo(TransferStatus.Failed));
                Assert.That(item.FailureReason, Does.Contain("permission denied"));
            });
        }

        [Test]
        public async Task AFailureDoesNotStopTheRestOfTheQueue()
        {
            // A permission error on the third of twenty files must not silently abandon the other
            // seventeen.
            using TransferQueue queue = new((item, _, _) =>
                item.SourcePath.EndsWith("bad", StringComparison.Ordinal)
                    ? throw new InvalidOperationException("nope")
                    : Task.CompletedTask);

            TransferItem good1 = Item("good1");
            TransferItem bad = Item("bad");
            TransferItem good2 = Item("good2");

            queue.EnqueueRange([good1, bad, good2]);
            await Idle(queue);

            Assert.Multiple(() =>
            {
                Assert.That(good1.Status, Is.EqualTo(TransferStatus.Succeeded));
                Assert.That(bad.Status, Is.EqualTo(TransferStatus.Failed));
                Assert.That(good2.Status, Is.EqualTo(TransferStatus.Succeeded));
            });
        }

        // ---- cancellation --------------------------------------------------------------

        [Test]
        public async Task CancellingAQueuedItemLeavesTheRestRunning()
        {
            using ManualResetEventSlim firstStarted = new();
            using ManualResetEventSlim release = new();

            using TransferQueue queue = new((item, _, ct) =>
            {
                if (item.SourcePath.EndsWith("slow", StringComparison.Ordinal))
                {
                    firstStarted.Set();
                    release.Wait(ct);
                }

                return Task.CompletedTask;
            });

            TransferItem slow = Item("slow");
            TransferItem doomed = Item("doomed");
            TransferItem survivor = Item("survivor");

            queue.EnqueueRange([slow, doomed, survivor]);
            firstStarted.Wait(TimeSpan.FromSeconds(5));

            queue.Cancel(doomed);
            release.Set();
            await Idle(queue);

            Assert.Multiple(() =>
            {
                Assert.That(doomed.Status, Is.EqualTo(TransferStatus.Cancelled));
                Assert.That(survivor.Status, Is.EqualTo(TransferStatus.Succeeded));
            });
        }

        [Test]
        public async Task CancellingTheRunningItemStopsIt()
        {
            using ManualResetEventSlim started = new();

            using TransferQueue queue = new(async (_, _, ct) =>
            {
                started.Set();
                await Task.Delay(Timeout.Infinite, ct);
            });

            TransferItem item = Item();
            queue.Enqueue(item);
            started.Wait(TimeSpan.FromSeconds(5));

            queue.Cancel(item);
            await Idle(queue);

            Assert.That(item.Status, Is.EqualTo(TransferStatus.Cancelled));
        }

        [Test]
        public async Task CancellingEverythingStopsTheQueue()
        {
            using ManualResetEventSlim started = new();

            using TransferQueue queue = new(async (_, _, ct) =>
            {
                started.Set();
                await Task.Delay(Timeout.Infinite, ct);
            });

            TransferItem running = Item("running");
            TransferItem waiting = Item("waiting");
            queue.EnqueueRange([running, waiting]);
            started.Wait(TimeSpan.FromSeconds(5));

            queue.CancelAll();
            await Idle(queue);

            Assert.Multiple(() =>
            {
                Assert.That(running.Status, Is.EqualTo(TransferStatus.Cancelled));
                Assert.That(waiting.Status, Is.EqualTo(TransferStatus.Cancelled));
            });
        }

        /// <summary>
        /// Something may still be producing items — a directory expansion — and it has to hear this, or
        /// "Cancel all" empties the queue and then watches it refill.
        /// </summary>
        [Test]
        public void CancellingTheQueueAnnouncesItself()
        {
            using TransferQueue queue = new((_, _, _) => Task.CompletedTask);
            int announced = 0;
            queue.AllCancelled += (_, _) => announced++;

            queue.CancelAll();

            Assert.That(announced, Is.EqualTo(1));
        }

        [Test]
        public async Task CancellingAFinishedItemIsHarmless()
        {
            using TransferQueue queue = new((_, _, _) => Task.CompletedTask);
            TransferItem item = Item();
            queue.Enqueue(item);
            await Idle(queue);

            queue.Cancel(item);

            Assert.That(item.Status, Is.EqualTo(TransferStatus.Succeeded));
        }

        // ---- progress and views ---------------------------------------------------------

        [Test]
        public async Task ProgressReachesTheItem()
        {
            using TransferQueue queue = new((_, progress, _) =>
            {
                progress.Report(40);
                progress.Report(100);
                return Task.CompletedTask;
            });

            TransferItem item = Item(size: 100);
            queue.Enqueue(item);
            await Idle(queue);

            Assert.Multiple(() =>
            {
                Assert.That(item.Transferred, Is.EqualTo(100));
                Assert.That(item.Fraction, Is.EqualTo(1.0));
            });
        }

        [Test]
        public void AnUnknownSizeHasNoFraction()
        {
            TransferItem item = new(TransferDirection.Download, "/remote/f", @"C:\f", size: -1);

            Assert.That(item.Fraction, Is.Null);
        }

        [Test]
        public async Task TheViewsSplitItemsByOutcome()
        {
            using TransferQueue queue = new((item, _, _) =>
                item.SourcePath.EndsWith("bad", StringComparison.Ordinal)
                    ? throw new InvalidOperationException("nope")
                    : Task.CompletedTask);

            queue.EnqueueRange([Item("good"), Item("bad")]);
            await Idle(queue);

            Assert.Multiple(() =>
            {
                Assert.That(queue.Succeeded, Has.Count.EqualTo(1));
                Assert.That(queue.Failed, Has.Count.EqualTo(1));
                Assert.That(queue.Queued, Is.Empty);
                Assert.That(queue.Items, Has.Count.EqualTo(2));
            });
        }

        [Test]
        public async Task ClearingFinishedLeavesOnlyOutstandingWork()
        {
            using TransferQueue queue = new((_, _, _) => Task.CompletedTask);
            queue.EnqueueRange([Item("a"), Item("b")]);
            await Idle(queue);

            queue.ClearFinished();

            Assert.That(queue.Items, Is.Empty);
        }

        [Test]
        public async Task ChangesAreAnnounced()
        {
            List<TransferStatus> seen = [];
            using TransferQueue queue = new((_, _, _) => Task.CompletedTask);
            queue.ItemChanged += (_, item) => { lock (seen) seen.Add(item.Status); };

            queue.Enqueue(Item());
            await Idle(queue);

            Assert.That(seen, Does.Contain(TransferStatus.Succeeded));
        }

        // ---- argument validation ----------------------------------------------------------

        [Test]
        public void ANullOperationIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new TransferQueue(null!));
        }

        [Test]
        public void ANullItemIsRejected()
        {
            using TransferQueue queue = new((_, _, _) => Task.CompletedTask);

            Assert.Throws<ArgumentNullException>(() => queue.Enqueue(null!));
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current = Volatile.Read(ref target);
            while (value > current)
            {
                int previous = Interlocked.CompareExchange(ref target, value, current);
                if (previous == current)
                    return;
                current = previous;
            }
        }
    }
}
