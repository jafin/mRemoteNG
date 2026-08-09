using System.Threading;
using System.Threading.Tasks;

namespace mRemoteNG.FileTransfer
{
    /// <summary>
    /// What to do about files that already exist at the destination.
    /// </summary>
    /// <remarks>
    /// One answer for a whole transfer, not one per file. Asking per file is what makes a tree transfer
    /// unusable — a hundred prompts is a hundred chances to click the wrong one.
    /// </remarks>
    public enum TransferConflictResolution
    {
        /// <summary>Replace every file that already exists.</summary>
        OverwriteAll = 0,

        /// <summary>Transfer none of the files that already exist.</summary>
        SkipAll = 1,

        /// <summary>Replace only where the source is newer than the destination.</summary>
        OverwriteIfNewer = 2,

        /// <summary>Abandon the transfer entirely.</summary>
        Cancel = 3
    }

    /// <summary>
    /// Asks the user how to resolve destination files that already exist.
    /// </summary>
    /// <remarks>
    /// Behind an interface for the same reason <see cref="UI.Controls.FileTransfer.IFilePanePrompts"/>
    /// is: a dialog is the one thing that cannot be exercised in a test. A fake answers directly, which
    /// is what makes the interesting cases assertable — that a transfer with no collisions never asks,
    /// that several collisions ask exactly once, and that skipping queues nothing.
    /// </remarks>
    public interface ITransferConflictResolver
    {
        /// <param name="source">The file about to be transferred.</param>
        /// <param name="destination">The file already at the destination.</param>
        Task<TransferConflictResolution> ResolveAsync(FileSystemEntry source,
                                                      FileSystemEntry destination,
                                                      CancellationToken cancellationToken = default);
    }
}
