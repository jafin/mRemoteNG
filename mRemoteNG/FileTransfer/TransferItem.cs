using System;
using System.Threading;

namespace mRemoteNG.FileTransfer
{
    public enum TransferDirection
    {
        Upload = 0,
        Download = 1
    }

    /// <summary>What a queued item does.</summary>
    /// <remarks>
    /// Separate from <see cref="TransferDirection"/> rather than a third member of it. A deletion has no
    /// direction, and overloading the field that drives the queue's ↑/↓ glyph would make the display
    /// claim something untrue about every deletion.
    /// </remarks>
    public enum TransferOperationKind
    {
        Transfer = 0,
        Delete = 1
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

        public TransferItem(TransferDirection direction,
                            string sourcePath,
                            string destinationPath,
                            long size,
                            TransferOperationKind kind = TransferOperationKind.Transfer)
        {
            ArgumentNullException.ThrowIfNull(sourcePath);
            ArgumentNullException.ThrowIfNull(destinationPath);

            Direction = direction;
            SourcePath = sourcePath;
            DestinationPath = destinationPath;
            Size = size;
            Kind = kind;
        }

        /// <summary>
        /// A deletion of <paramref name="entry"/> on the given side.
        /// </summary>
        /// <remarks>
        /// Keeps the entry rather than just its path. What has to be deleted — a file, a directory, a
        /// link — is carried on the entry, and rebuilding it from a path and a size gets both wrong: an
        /// empty file is indistinguishable from a directory by size, and a reconstructed entry loses
        /// <c>IsSymbolicLink</c>, which is exactly what stops a link to a directory being deleted as
        /// one. <paramref name="direction"/> only says which pane owns it.
        /// </remarks>
        public static TransferItem Deletion(TransferDirection direction, FileSystemEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            return new TransferItem(direction, entry.FullPath, string.Empty,
                                    entry.IsDirectory ? 0 : entry.Length,
                                    TransferOperationKind.Delete)
            {
                DeleteTarget = entry
            };
        }

        /// <summary>What this item deletes. Non-null exactly when <see cref="Kind"/> is a deletion.</summary>
        public FileSystemEntry? DeleteTarget { get; private init; }

        public TransferDirection Direction { get; }

        /// <summary>Whether this item moves something or removes it.</summary>
        public TransferOperationKind Kind { get; }

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
            Kind == TransferOperationKind.Delete
                ? $"Delete {SourcePath} ({Status})"
                : $"{Direction} {SourcePath} -> {DestinationPath} ({Status})";
    }
}
