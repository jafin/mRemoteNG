using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.FileTransfer;
using mRemoteNG.UI.Controls.FileTransfer;
using NUnit.Framework;

namespace mRemoteNGTests.FileTransfer
{
    /// <summary>
    /// Covers <c>specs/file-manager-reconnection/spec.md</c>. The decisions are separated from the
    /// control precisely so they can be asserted here, with no window and no server.
    /// </summary>
    [TestFixture]
    public class PaneReconnectionTests
    {
        private sealed class FakeConnection(bool connected, bool reconnectSucceeds = true) : IPaneConnection
        {
            public bool IsConnected { get; private set; } = connected;

            public int ReconnectAttempts { get; private set; }

            public event EventHandler? ConnectionChanged;

            public Task<bool> ReconnectAsync(CancellationToken cancellationToken = default)
            {
                ReconnectAttempts++;

                if (reconnectSucceeds)
                    IsConnected = true;

                ConnectionChanged?.Invoke(this, EventArgs.Empty);
                return Task.FromResult(reconnectSucceeds);
            }
        }

        // ---- when to list ------------------------------------------------------------

        /// <summary>The local pane, which has nothing to reconnect to, must list as it always did.</summary>
        [Test]
        public async Task WithNoConnectionThePaneJustLists() =>
            Assert.That(await FilePaneControl.ShouldListAsync(null), Is.True);

        [Test]
        public async Task AConnectedPaneListsWithoutReconnecting()
        {
            FakeConnection connection = new(connected: true);

            bool shouldList = await FilePaneControl.ShouldListAsync(connection);

            Assert.Multiple(() =>
            {
                Assert.That(shouldList, Is.True);
                Assert.That(connection.ReconnectAttempts, Is.Zero);
            });
        }

        [Test]
        public async Task ADisconnectedPaneReconnectsThenLists()
        {
            FakeConnection connection = new(connected: false);

            bool shouldList = await FilePaneControl.ShouldListAsync(connection);

            Assert.Multiple(() =>
            {
                Assert.That(shouldList, Is.True);
                Assert.That(connection.ReconnectAttempts, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// Listing anyway would fail with the same "not connected" error the reconnect just reported,
        /// giving one gesture two errors.
        /// </summary>
        [Test]
        public async Task AFailedReconnectDoesNotThenList()
        {
            FakeConnection connection = new(connected: false, reconnectSucceeds: false);

            bool shouldList = await FilePaneControl.ShouldListAsync(connection);

            Assert.Multiple(() =>
            {
                Assert.That(shouldList, Is.False);
                Assert.That(connection.ReconnectAttempts, Is.EqualTo(1));
            });
        }

        // ---- one at a time -----------------------------------------------------------

        [Test]
        public void OnlyOneOperationRunsAtATime()
        {
            SingleFlight flight = new();

            Assert.Multiple(() =>
            {
                Assert.That(flight.TryEnter(), Is.True);
                Assert.That(flight.TryEnter(), Is.False, "a second caller must be turned away, not queued");
            });
        }

        [Test]
        public void TheSlotIsReusableOnceReleased()
        {
            SingleFlight flight = new();

            flight.TryEnter();
            flight.Exit();

            Assert.Multiple(() =>
            {
                Assert.That(flight.TryEnter(), Is.True);
                Assert.That(flight.IsRunning, Is.True);
            });
        }

        [Test]
        public void ReleasingAnUnclaimedSlotIsHarmless()
        {
            SingleFlight flight = new();

            Assert.DoesNotThrow(flight.Exit);
            Assert.That(flight.IsRunning, Is.False);
        }

        [Test]
        public async Task ConcurrentCallersLeaveExactlyOneHolder()
        {
            SingleFlight flight = new();
            int admitted = 0;

            await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
            {
                if (flight.TryEnter())
                    Interlocked.Increment(ref admitted);
            })));

            Assert.That(admitted, Is.EqualTo(1));
        }
    }
}
