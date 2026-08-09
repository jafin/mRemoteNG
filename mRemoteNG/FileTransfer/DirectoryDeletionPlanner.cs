using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace mRemoteNG.FileTransfer;

/// <summary>
/// Turns a selected entry into the entries to delete, deepest first.
/// </summary>
/// <remarks>
/// <para>
/// Post-order is the whole of the ordering guarantee. A directory can only be removed once it is
/// empty, and the queue runs one item at a time in the order it was given them — so yielding a
/// directory's contents before the directory itself is all it takes. No dependency tracking, no
/// retry pass, no second attempt at the end.
/// </para>
/// <para>
/// Not shared with <see cref="DirectoryTransferExpander"/>. That one is pre-order by necessity — a
/// destination directory has to exist before anything goes in it — and carries a destination browser
/// and conflict resolution. One class parameterised by two orderings, with half its constructor
/// optional, would be harder to read than two small walkers that each do one thing.
/// </para>
/// </remarks>
public sealed class DirectoryDeletionPlanner
{
    private readonly IFileSystemBrowser _browser;
    private readonly int _maximumDepth;

    /// <param name="maximumDepth">
    /// Overridable so a test can reach the limit without building a tree that deep.
    /// </param>
    public DirectoryDeletionPlanner(IFileSystemBrowser browser,
        int maximumDepth = DirectoryTransferExpander.DefaultMaximumDepth)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);

        _browser = browser;
        _maximumDepth = maximumDepth;
    }

    /// <summary>
    /// Raised for anything deliberately not deleted, with a message fit to show the user.
    /// </summary>
    public event EventHandler<string>? Skipped;

    /// <summary>
    /// Yields every entry to delete, children before parents.
    /// </summary>
    /// <remarks>
    /// Streamed rather than collected, so the queue fills and the first deletions start while the
    /// rest of the tree is still being read. A branch's entries are produced as the walk unwinds.
    /// </remarks>
    public async IAsyncEnumerable<FileSystemEntry> PlanAsync(
        FileSystemEntry entry,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        cancellationToken.ThrowIfCancellationRequested();

        // A link is deleted as itself, never followed. Descending one would destroy data outside the
        // tree the user selected — the single mistake here that cannot be walked back — and is also
        // what would let a link to an ancestor turn a folder deletion into an unbounded one.
        // Unlike a transfer, no following stat is needed: deleting a link deletes the link, whatever
        // it points at, so the question never arises.
        if (!entry.IsDirectory || entry.IsSymbolicLink)
        {
            yield return entry;
            yield break;
        }

        await foreach (FileSystemEntry planned in
                       WalkAsync(entry, depth: 1, cancellationToken)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return planned;
        }
    }

    private async IAsyncEnumerable<FileSystemEntry> WalkAsync(
        FileSystemEntry directory,
        int depth,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (depth > _maximumDepth)
        {
            Report(string.Format(CultureInfo.CurrentCulture,
                "Stopped at {0}: more than {1} directories deep, so it was not deleted. Delete it directly to remove its contents.",
                directory.FullPath, _maximumDepth));
            yield break;
        }

        IReadOnlyList<FileSystemEntry>? children =
            await ListAsync(directory, cancellationToken).ConfigureAwait(false);

        // Without the listing there is no way to empty the directory, so it is left alone rather
        // than queued to fail. Its siblings are unaffected.
        if (children is null)
            yield break;

        foreach (FileSystemEntry child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!child.IsDirectory || child.IsSymbolicLink)
            {
                yield return child;
                continue;
            }

            await foreach (FileSystemEntry planned in
                           WalkAsync(child, depth + 1, cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return planned;
            }
        }

        // Last, once everything above has been queued ahead of it.
        yield return directory;
    }

    private async Task<IReadOnlyList<FileSystemEntry>?> ListAsync(FileSystemEntry directory,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _browser.ListAsync(directory.FullPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Report($"Skipped {directory.FullPath}: {ex.Message}");
            return null;
        }
    }

    private void Report(string message) => Skipped?.Invoke(this, message);
}