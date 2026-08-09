using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace mRemoteNG.FileTransfer
{
    /// <summary>
    /// Turns a selected entry into the set of files to move, creating the destination directories it
    /// needs along the way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written once against <see cref="IFileSystemBrowser"/> and parameterised by which side is which:
    /// an upload is <c>(local, remote)</c> and a download is <c>(remote, local)</c>. Both directions get
    /// the same depth limits, link rules and error recovery, and every one of them can be exercised
    /// against a fake browser without a server.
    /// </para>
    /// <para>
    /// One instance per transfer gesture. The answer to the overwrite question is cached on the
    /// instance, which is what makes it "asked once per transfer" even when the user selected several
    /// directories at once — and what makes the next transfer ask again.
    /// </para>
    /// </remarks>
    public sealed class DirectoryTransferExpander
    {
        /// <summary>
        /// How deep expansion will descend before giving up on a branch.
        /// </summary>
        /// <remarks>
        /// Hard-coded for now; intended to become a setting. This is the backstop, not the primary
        /// defence against a cycle — not descending into links is. Unlike that rule, this is a limit
        /// real trees can reach, which is why hitting it is reported rather than silent.
        /// </remarks>
        public const int DefaultMaximumDepth = 16;

        /// <summary>
        /// How far apart two timestamps must be before one counts as newer than the other.
        /// </summary>
        /// <remarks>
        /// Hard-coded for now; intended to become a setting. It earns its place twice: FAT records
        /// timestamps to two-second granularity, so an exact comparison would call a file newer than its
        /// own identical copy, and two machines' clocks are never exactly aligned, so a sub-second
        /// difference says nothing about which file is really more recent. It does not rescue a
        /// genuinely skewed clock, and no tolerance small enough to be useful would.
        /// </remarks>
        public static readonly TimeSpan TimestampSkewTolerance = TimeSpan.FromSeconds(2);

        private readonly IFileSystemBrowser _source;
        private readonly IFileSystemBrowser _destination;
        private readonly ITransferConflictResolver _resolver;
        private readonly int _maximumDepth;

        /// <summary>
        /// What already exists in each destination directory, keyed by that directory's path.
        /// </summary>
        /// <remarks>
        /// One listing per destination directory, not one stat per file. A directory this expander
        /// created is empty, so it is recorded as such without being listed at all — which is the whole
        /// saving on the case that matters, a large tree going somewhere new.
        /// </remarks>
        private readonly Dictionary<string, Dictionary<string, FileSystemEntry>> _existingEntries;

        private TransferConflictResolution? _resolution;
        private bool _abandoned;

        /// <param name="maximumDepth">
        /// Overridable so a test can reach the limit without building a tree that deep.
        /// </param>
        public DirectoryTransferExpander(IFileSystemBrowser source,
                                         IFileSystemBrowser destination,
                                         ITransferConflictResolver resolver,
                                         int maximumDepth = DefaultMaximumDepth)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(resolver);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);

            _source = source;
            _destination = destination;
            _resolver = resolver;
            _maximumDepth = maximumDepth;

            _existingEntries = new Dictionary<string, Dictionary<string, FileSystemEntry>>(NameComparer(destination));
        }

        /// <summary>
        /// Raised for anything deliberately not transferred, with a message fit to show the user.
        /// </summary>
        public event EventHandler<string>? Skipped;

        /// <summary>
        /// How many files were passed over because they already existed at the destination.
        /// </summary>
        /// <remarks>
        /// Reported once at the end rather than per file. It is also what makes a skewed clock visible:
        /// "overwrite if newer" quietly copying nothing looks like success until this number does not.
        /// </remarks>
        public int SkippedExistingCount { get; private set; }

        /// <summary>
        /// Whether the user cancelled at the overwrite prompt, abandoning the whole transfer.
        /// </summary>
        public bool WasCancelledByUser { get; private set; }

        /// <summary>
        /// Yields every file that should move, in the order it was found.
        /// </summary>
        /// <remarks>
        /// Streamed rather than returned as a list so the queue fills — and the first file starts
        /// moving — while the rest of the tree is still being walked. On a large tree over a slow link
        /// the difference is minutes of an apparently idle queue.
        /// </remarks>
        /// <param name="entry">The selected file or directory.</param>
        /// <param name="destinationDirectory">The directory it is being transferred into.</param>
        public async IAsyncEnumerable<TransferPlanItem> ExpandAsync(
            FileSystemEntry entry,
            string destinationDirectory,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(destinationDirectory);

            if (_abandoned)
                yield break;

            bool isDirectory = entry.IsDirectory;

            if (entry.IsSymbolicLink)
            {
                // A listing reports the link, not its target, so this is the only place the difference
                // can be established. Never descended into either way; the question is only whether the
                // thing at the other end is a file worth copying.
                if (await ResolvesToDirectoryAsync(entry, cancellationToken).ConfigureAwait(false))
                {
                    Report($"Skipped {entry.Name}: it is a link to a directory, and links are not followed.");
                    yield break;
                }

                isDirectory = false;
            }

            if (!isDirectory)
            {
                TransferPlanItem? single =
                    await PlanFileAsync(entry, destinationDirectory, cancellationToken).ConfigureAwait(false);

                if (single is not null)
                    yield return single;

                yield break;
            }

            string destinationRoot = _destination.Combine(destinationDirectory, entry.Name);

            await foreach (TransferPlanItem item in
                           WalkAsync(entry.FullPath, destinationRoot, depth: 1, cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return item;
            }
        }

        private async IAsyncEnumerable<TransferPlanItem> WalkAsync(
            string sourceDirectory,
            string destinationDirectory,
            int depth,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Created before anything inside it is yielded, so ordering is right by construction and a
            // directory that is empty in the source still appears at the destination.
            if (!await PrepareDestinationAsync(sourceDirectory, destinationDirectory, cancellationToken)
                    .ConfigureAwait(false))
                yield break;

            IReadOnlyList<FileSystemEntry>? entries =
                await ListSourceAsync(sourceDirectory, cancellationToken).ConfigureAwait(false);

            if (entries is null)
                yield break;

            foreach (FileSystemEntry entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_abandoned)
                    yield break;

                bool isDirectory = entry.IsDirectory;

                if (entry.IsSymbolicLink)
                {
                    if (await ResolvesToDirectoryAsync(entry, cancellationToken).ConfigureAwait(false))
                    {
                        Report($"Skipped {entry.FullPath}: it is a link to a directory, and links are not followed.");
                        continue;
                    }

                    isDirectory = false;
                }

                if (isDirectory)
                {
                    if (depth >= _maximumDepth)
                    {
                        Report(string.Format(CultureInfo.CurrentCulture,
                                             "Stopped at {0}: more than {1} directories deep, so it was not expanded in full. Transfer it directly to include its contents.",
                                             entry.FullPath, _maximumDepth));
                        continue;
                    }

                    string childDestination = _destination.Combine(destinationDirectory, entry.Name);

                    await foreach (TransferPlanItem item in
                                   WalkAsync(entry.FullPath, childDestination, depth + 1, cancellationToken)
                                       .WithCancellation(cancellationToken)
                                       .ConfigureAwait(false))
                    {
                        yield return item;
                    }

                    continue;
                }

                TransferPlanItem? planned =
                    await PlanFileAsync(entry, destinationDirectory, cancellationToken).ConfigureAwait(false);

                if (planned is not null)
                    yield return planned;
            }
        }

        /// <summary>
        /// Creates the destination directory and records what is already in it.
        /// </summary>
        /// <returns><see langword="false"/> when this branch cannot be transferred at all.</returns>
        private async Task<bool> PrepareDestinationAsync(string sourceDirectory,
                                                         string destinationDirectory,
                                                         CancellationToken cancellationToken)
        {
            if (_existingEntries.ContainsKey(destinationDirectory))
                return true;

            bool created;

            try
            {
                created = await _destination.EnsureDirectoryAsync(destinationDirectory, cancellationToken)
                                            .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The whole branch is abandoned rather than queued to fail file by file. Thousands of
                // items that cannot succeed is not a report, it is noise.
                Report($"Skipped {sourceDirectory}: could not create {destinationDirectory}. {ex.Message}");
                return false;
            }

            if (created)
            {
                // Nothing can collide in a directory that did not exist a moment ago, so it is recorded
                // as empty without being listed. This is what keeps a large tree to one round trip per
                // directory instead of one per file.
                _existingEntries[destinationDirectory] = new Dictionary<string, FileSystemEntry>(NameComparer(_destination));
                return true;
            }

            return await RecordExistingAsync(sourceDirectory, destinationDirectory, cancellationToken)
                       .ConfigureAwait(false);
        }

        private async Task<bool> RecordExistingAsync(string sourceDirectory,
                                                     string destinationDirectory,
                                                     CancellationToken cancellationToken)
        {
            try
            {
                IReadOnlyList<FileSystemEntry> existing =
                    await _destination.ListAsync(destinationDirectory, cancellationToken).ConfigureAwait(false);

                Dictionary<string, FileSystemEntry> byName = new(existing.Count, NameComparer(_destination));
                foreach (FileSystemEntry item in existing)
                    byName[item.Name] = item;

                _existingEntries[destinationDirectory] = byName;
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Without the listing there is no way to tell a collision from a new file, and writing
                // blind is exactly what the overwrite prompt exists to prevent.
                Report($"Skipped {sourceDirectory}: could not read {destinationDirectory} to check for existing files. {ex.Message}");
                return false;
            }
        }

        private async Task<IReadOnlyList<FileSystemEntry>?> ListSourceAsync(string sourceDirectory,
                                                                           CancellationToken cancellationToken)
        {
            try
            {
                return await _source.ListAsync(sourceDirectory, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One protected subdirectory in a large tree is normal. Losing the other branches over
                // it would make the feature useless exactly where it is most useful.
                Report($"Skipped {sourceDirectory}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Decides whether one file should move, asking about a collision if this is the first.
        /// </summary>
        /// <returns><see langword="null"/> when the file should not be transferred.</returns>
        private async Task<TransferPlanItem?> PlanFileAsync(FileSystemEntry file,
                                                            string destinationDirectory,
                                                            CancellationToken cancellationToken)
        {
            string destinationPath = _destination.Combine(destinationDirectory, file.Name);

            if (!_existingEntries.TryGetValue(destinationDirectory, out Dictionary<string, FileSystemEntry>? existing))
            {
                // The destination of a directly selected file: the pane's current directory, which was
                // never walked into and so has not been listed yet.
                if (!await RecordExistingAsync(file.FullPath, destinationDirectory, cancellationToken)
                        .ConfigureAwait(false))
                    return null;

                existing = _existingEntries[destinationDirectory];
            }

            if (!existing.TryGetValue(file.Name, out FileSystemEntry? clash))
                return new TransferPlanItem(file.FullPath, destinationPath, file.Length);

            if (clash.IsDirectory)
            {
                // Not a question the overwrite prompt can answer: a file cannot replace a directory.
                Report($"Skipped {file.FullPath}: a directory of that name already exists at the destination.");
                return null;
            }

            TransferConflictResolution resolution =
                await ResolveAsync(file, clash, cancellationToken).ConfigureAwait(false);

            switch (resolution)
            {
                case TransferConflictResolution.OverwriteAll:
                    return new TransferPlanItem(file.FullPath, destinationPath, file.Length);

                case TransferConflictResolution.OverwriteIfNewer:
                    if (file.LastWriteTime - clash.LastWriteTime > TimestampSkewTolerance)
                        return new TransferPlanItem(file.FullPath, destinationPath, file.Length);

                    SkippedExistingCount++;
                    return null;

                case TransferConflictResolution.Cancel:
                    _abandoned = true;
                    WasCancelledByUser = true;
                    return null;

                default:
                    SkippedExistingCount++;
                    return null;
            }
        }

        /// <summary>
        /// Returns the answer for this transfer, asking only the first time.
        /// </summary>
        private async Task<TransferConflictResolution> ResolveAsync(FileSystemEntry source,
                                                                    FileSystemEntry destination,
                                                                    CancellationToken cancellationToken)
        {
            if (_resolution is { } answered)
                return answered;

            TransferConflictResolution resolution =
                await _resolver.ResolveAsync(source, destination, cancellationToken).ConfigureAwait(false);

            _resolution = resolution;
            return resolution;
        }

        /// <summary>
        /// Whether a link lands on a directory, treating a failure to find out as "not a directory".
        /// </summary>
        /// <remarks>
        /// A broken link, or one whose target cannot be stat'd, must not stop the walk. Answering false
        /// means it is queued as a file and fails as one item if it really cannot be read, which is
        /// visible and bounded — unlike descending into something unknown.
        /// </remarks>
        private async Task<bool> ResolvesToDirectoryAsync(FileSystemEntry entry, CancellationToken cancellationToken)
        {
            try
            {
                return await _source.LinkTargetIsDirectoryAsync(entry.FullPath, cancellationToken)
                                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static StringComparer NameComparer(IFileSystemBrowser browser) =>
            browser.PathsAreCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        private void Report(string message) => Skipped?.Invoke(this, message);
    }
}
