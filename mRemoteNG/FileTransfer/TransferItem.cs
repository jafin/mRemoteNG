using System;
using System.Threading;

namespace mRemoteNG.FileTransfer
{
    public enum TransferDirection
    {
        Upload = 0,
        Download = 1
    }

    public enum TransferStatus
    {
        Queued = 0,
        Running = 1,
        Succeeded = 2,
        Failed = 3,
        Cancelled = 4
    }

    /// <summary>
    /// One transfer in the queue.
    /// </summary>
    /// <remarks>
    /// Mutable, and deliberately not a record: the queue view holds these while their progress and
    /// status change, so replacing the instance on every progress report would mean rebuilding rows
    /// hundreds of times a second.
    /// </remarks>
    public sealed class TransferItem
    {
        private long _transferred;

        public TransferItem(TransferDirection direction, string sourcePath, string destinationPath, long size)
        {
            ArgumentNullException.ThrowIfNull(sourcePath);
            ArgumentNullException.ThrowIfNull(destinationPath);

            Direction = direction;
            SourcePath = sourcePath;
            DestinationPath = destinationPath;
            Size = size;
        }

        public TransferDirection Direction { get; }

        public string SourcePath { get; }

        public string DestinationPath { get; }

        /// <summary>Expected size in bytes, or -1 when it is not known.</summary>
        public long Size { get; }

        public TransferStatus Status { get; internal set; } = TransferStatus.Queued;

        /// <summary>Why the item failed. Empty unless <see cref="Status"/> is failed.</summary>
        public string FailureReason { get; internal set; } = string.Empty;

        public long Transferred => Interlocked.Read(ref _transferred);

        /// <summary>Completed fraction, or <see langword="null"/> when the size is unknown.</summary>
        public double? Fraction => Size > 0 ? Math.Min(1.0, (double)Transferred / Size) : null;

        /// <summary>Whether the item has reached a state it will not leave.</summary>
        public bool IsFinished =>
            Status is TransferStatus.Succeeded or TransferStatus.Failed or TransferStatus.Cancelled;

        internal void SetTransferred(long value) => Interlocked.Exchange(ref _transferred, value);

        public override string ToString() =>
            $"{Direction} {SourcePath} -> {DestinationPath} ({Status})";
    }
}
