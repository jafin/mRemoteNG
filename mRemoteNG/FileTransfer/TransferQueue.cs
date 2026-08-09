using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace mRemoteNG.FileTransfer
{
    /// <summary>
    /// Performs one queued transfer.
    /// </summary>
    /// <param name="item">The item to move.</param>
    /// <param name="progress">Reports bytes transferred so far.</param>
    /// <returns>A task completing when the transfer has finished.</returns>
    public delegate Task TransferOperation(TransferItem item,
                                           IProgress<long> progress,
                                           CancellationToken cancellationToken);

    /// <summary>
    /// Runs queued transfers in the background, one at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A queue rather than a blocking transfer per action is what makes the file manager usable:
    /// selecting twenty files and carrying on browsing is the normal case.
    /// </para>
    /// <para>
    /// One at a time on purpose. Concurrent transfers over a single SFTP channel contend for the
    /// same window and usually make the whole set slower, and a single running item keeps
    /// cancellation and progress reporting honest. Raising this later is a change to the runner, not
    /// to the shape of the queue.
    /// </para>
    /// <para>
    /// The runner is started by <see cref="Enqueue"/> and stops when the queue empties, so an idle
    /// file manager holds no background task.
    /// </para>
    /// </remarks>
    public sealed class TransferQueue : IDisposable
    {
        private readonly TransferOperation _operation;
        private readonly List<TransferItem> _items = [];
        private readonly Dictionary<TransferItem, CancellationTokenSource> _running = [];
        private readonly Lock _gate = new();

        private CancellationTokenSource _queueCancellation = new();
        private Task _runner = Task.CompletedTask;
        private bool _disposed;

        public TransferQueue(TransferOperation operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            _operation = operation;
        }

        /// <summary>Raised when an item is added, starts, progresses or finishes.</summary>
        public event EventHandler<TransferItem>? ItemChanged;

        /// <summary>Raised when the queue finishes everything it holds.</summary>
        public event EventHandler? Drained;

        /// <summary>Every item, in the order queued, whatever its status.</summary>
        public IReadOnlyList<TransferItem> Items
        {
            get
            {
                lock (_gate)
                    return _items.ToArray();
            }
        }

        public IReadOnlyList<TransferItem> Queued => Filter(i => i.Status is TransferStatus.Queued or TransferStatus.Running);

        public IReadOnlyList<TransferItem> Failed => Filter(i => i.Status is TransferStatus.Failed);

        public IReadOnlyList<TransferItem> Succeeded => Filter(i => i.Status is TransferStatus.Succeeded);

        /// <summary>Whether the runner currently has work.</summary>
        public bool IsRunning
        {
            get
            {
                lock (_gate)
                    return !_runner.IsCompleted;
            }
        }

        public void Enqueue(TransferItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            ObjectDisposedException.ThrowIf(_disposed, this);

            lock (_gate)
            {
                _items.Add(item);

                if (_runner.IsCompleted)
                {
                    if (_queueCancellation.IsCancellationRequested)
                    {
                        _queueCancellation.Dispose();
                        _queueCancellation = new CancellationTokenSource();
                    }

                    _runner = Task.Run(() => RunAsync(_queueCancellation.Token));
                }
            }

            Raise(item);
        }

        public void EnqueueRange(IEnumerable<TransferItem> items)
        {
            ArgumentNullException.ThrowIfNull(items);

            foreach (TransferItem item in items)
                Enqueue(item);
        }

        /// <summary>
        /// Cancels one item. A queued item never starts; a running one is stopped. The rest of the
        /// queue is unaffected.
        /// </summary>
        public void Cancel(TransferItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            CancellationTokenSource? running = null;

            lock (_gate)
            {
                if (item.IsFinished)
                    return;

                if (_running.TryGetValue(item, out CancellationTokenSource? cts))
                {
                    running = cts;
                }
                else
                {
                    item.Status = TransferStatus.Cancelled;
                }
            }

            // Cancel outside the lock: the continuation marks the item finished and takes it.
            running?.Cancel();

            if (running is null)
                Raise(item);
        }

        /// <summary>Cancels the running item and stops anything else from starting.</summary>
        public void CancelAll()
        {
            List<TransferItem> stillQueued;
            CancellationTokenSource cancellation;

            lock (_gate)
            {
                cancellation = _queueCancellation;
                stillQueued = _items.Where(i => i.Status == TransferStatus.Queued).ToList();
                foreach (TransferItem item in stillQueued)
                    item.Status = TransferStatus.Cancelled;
            }

            cancellation.Cancel();

            foreach (TransferItem item in stillQueued)
                Raise(item);
        }

        /// <summary>Removes finished items, leaving anything queued or running.</summary>
        public void ClearFinished()
        {
            lock (_gate)
                _items.RemoveAll(i => i.IsFinished);
        }

        /// <summary>Waits for the runner to drain. For tests; the UI never blocks on this.</summary>
        public Task WaitForIdleAsync()
        {
            lock (_gate)
                return _runner;
        }

        private async Task RunAsync(CancellationToken queueToken)
        {
            while (true)
            {
                TransferItem? next;

                lock (_gate)
                {
                    next = _items.FirstOrDefault(i => i.Status == TransferStatus.Queued);
                    if (next is null)
                        break;

                    next.Status = TransferStatus.Running;
                }

                await RunOneAsync(next, queueToken).ConfigureAwait(false);
                Raise(next);
            }

            Drained?.Invoke(this, EventArgs.Empty);
        }

        private async Task RunOneAsync(TransferItem item, CancellationToken queueToken)
        {
            using CancellationTokenSource itemCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(queueToken);

            lock (_gate)
                _running[item] = itemCancellation;

            Raise(item);

            try
            {
                // Deliberately not Progress<T>. That posts to the captured synchronization context,
                // so reports arrive asynchronously and unordered — a final "100 bytes" can land
                // after the item has already been marked succeeded, leaving a finished transfer
                // showing partial progress. Reporting inline keeps the item's state truthful at
                // every moment; the UI marshals when it handles ItemChanged, which it must do
                // anyway because that event already comes off a background thread.
                ImmediateProgress progress = new(this, item);

                await _operation(item, progress, itemCancellation.Token).ConfigureAwait(false);

                item.Status = TransferStatus.Succeeded;
            }
            catch (OperationCanceledException)
            {
                item.Status = TransferStatus.Cancelled;
            }
            catch (Exception ex)
            {
                // One failure must not stop the queue: a permission error on the third of twenty
                // files should not silently abandon the other seventeen.
                item.Status = TransferStatus.Failed;
                item.FailureReason = ex.Message;
            }
            finally
            {
                lock (_gate)
                    _running.Remove(item);
            }
        }

        private IReadOnlyList<TransferItem> Filter(Func<TransferItem, bool> predicate)
        {
            lock (_gate)
                return _items.Where(predicate).ToArray();
        }

        private void Raise(TransferItem item) => ItemChanged?.Invoke(this, item);

        /// <summary>Applies a progress report on the reporting thread, in order.</summary>
        private sealed class ImmediateProgress(TransferQueue queue, TransferItem item) : IProgress<long>
        {
            public void Report(long value)
            {
                item.SetTransferred(value);
                queue.Raise(item);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            CancelAll();
            _queueCancellation.Dispose();
        }
    }
}
