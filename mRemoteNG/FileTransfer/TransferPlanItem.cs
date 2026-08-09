namespace mRemoteNG.FileTransfer
{
    /// <summary>
    /// One file a directory expansion decided to move.
    /// </summary>
    /// <remarks>
    /// Deliberately not a <see cref="TransferItem"/>. This is the expander's output — a decision about
    /// what should move — while a <see cref="TransferItem"/> is the queue's mutable record of a move in
    /// progress. Keeping them apart is what lets the expander be tested without a queue.
    /// </remarks>
    /// <param name="SourcePath">The full path to read from, in the source filesystem's syntax.</param>
    /// <param name="DestinationPath">The full path to write to, in the destination's syntax.</param>
    /// <param name="Length">Size in bytes, for progress reporting.</param>
    public sealed record TransferPlanItem(string SourcePath, string DestinationPath, long Length);
}
