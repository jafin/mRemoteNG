## 1. Model and browser surface

- [x] 1.1 Append `bool IsSymbolicLink = false` to the `FileSystemEntry` record, documented like its
      siblings, so existing positional construction sites keep compiling.
- [x] 1.2 Populate it in `RemoteFileSystemBrowser.Describe` from `SftpEntry.IsSymbolicLink` (currently
      dropped) and in `LocalFileSystemBrowser.Describe` from `FileAttributes.ReparsePoint`.
- [x] 1.3 Add `Task<bool> EnsureDirectoryAsync(string path, CancellationToken)` to `IFileSystemBrowser`,
      returning whether it had to create the directory and documenting that an existing one is success.
- [x] 1.4 Add `Task<bool> LinkTargetIsDirectoryAsync(string path, CancellationToken)` to
      `IFileSystemBrowser`, documenting that it follows the link.
- [x] 1.5 Add `bool PathsAreCaseSensitive` to `IFileSystemBrowser` — false for local, true for remote —
      and have `FileManagerTab` pass `browser.PathsAreCaseSensitive` to `FilePaneController` instead of
      the hand-written `caseSensitivePaths` literals.

## 2. Remote implementation

- [x] 2.1 Add `Task<bool> ExistsAsync(string path, CancellationToken)` to `ISftpSession`, implemented
      over `SftpClient.ExistsAsync` and guarded by `RequireConnected`.
- [x] 2.2 Add `Task<bool> ResolvesToDirectoryAsync(string path, CancellationToken)` to `ISftpSession`,
      implemented over `SftpClient.GetAttributesAsync` (a following stat, unlike the listing's lstat).
- [x] 2.3 Implement `EnsureDirectoryAsync` on `RemoteFileSystemBrowser`: check `ExistsAsync`, create
      only when absent, and treat an "already exists" failure from the race as success.
- [x] 2.4 Implement `LinkTargetIsDirectoryAsync` on `RemoteFileSystemBrowser` over
      `ResolvesToDirectoryAsync`.

## 3. Local implementation

- [x] 3.1 Implement `EnsureDirectoryAsync` on `LocalFileSystemBrowser` over `Directory.CreateDirectory`,
      which is already idempotent, on a worker thread like its neighbours.
- [x] 3.2 Implement `LinkTargetIsDirectoryAsync` on `LocalFileSystemBrowser` over `Directory.Exists`,
      which follows a reparse point on Windows.

## 4. The expander

- [x] 4.1 Add `TransferPlanItem` (source path, destination path, length) in `mRemoteNG/FileTransfer/`.
- [x] 4.2 Add `DirectoryTransferExpander` taking a source browser and a destination browser, exposing
      `IAsyncEnumerable<TransferPlanItem> ExpandAsync(FileSystemEntry root, string destinationDirectory,
      CancellationToken)` and a `Skipped` event carrying a message.
- [x] 4.3 Declare the two tuning values together as named constants on the expander —
      `DefaultMaximumDepth = 16` and `TimestampSkewTolerance = TimeSpan.FromSeconds(2)` — each with a
      comment saying it is hard-coded for now and intended to become a setting. Take the depth as an
      optional constructor parameter defaulting to the constant, so tests can drive it without changing
      it globally.
- [x] 4.4 Track position as a list of name segments and build each destination path with the
      *destination* browser's `Combine`, never by concatenating a source path (design D2).
- [x] 4.5 Create each destination directory pre-order via `EnsureDirectoryAsync` before yielding any of
      the files inside it, so empty directories are reproduced and ordering is correct by construction.
      When it reports the directory already existed, list it once and keep the result as that
      directory's collision map; when it created it, skip the listing — nothing in it can collide.
- [x] 4.6 On a directory whose destination cannot be created, raise `Skipped` and abandon that branch
      only — do not yield items beneath it, and continue with the rest of the tree.
- [x] 4.7 On a directory that cannot be listed, raise `Skipped` and continue with the remaining branches.
- [x] 4.8 For an entry with `IsSymbolicLink`, call `LinkTargetIsDirectoryAsync`: yield it as a file when
      it resolves to a file, raise `Skipped` when it resolves to a directory. Never descend into a link.
- [x] 4.9 Stop descending at `DefaultMaximumDepth` and raise `Skipped` saying the tree was not expanded
      in full, naming the directory so the user can transfer that subtree directly.
- [x] 4.10 Honour the cancellation token at every listing and between yields.

## 5. Conflict resolution

- [x] 5.1 Add `TransferConflictResolution` (`OverwriteAll`, `SkipAll`, `OverwriteIfNewer`, `Cancel`) and
      `ITransferConflictResolver` with `Task<TransferConflictResolution> ResolveAsync(FileSystemEntry
      source, FileSystemEntry destination, CancellationToken)`, following the `IFilePanePrompts` pattern.
- [x] 5.2 In the expander, look each candidate file up in its directory's collision map, comparing names
      with the destination's `PathsAreCaseSensitive`.
- [x] 5.3 On the first collision, call the resolver once and cache the answer for the rest of the run;
      never call it when there are no collisions.
- [x] 5.4 Apply the cached answer: queue the file for `OverwriteAll`; drop it for `SkipAll`; for
      `OverwriteIfNewer` queue it only when the source's last-write time exceeds the destination's by
      more than `TimestampSkewTolerance`; for `Cancel` stop the walk and yield nothing further.
- [x] 5.5 When a file collides with a *directory* of the same name, raise `Skipped` and do not queue it
      or ask the resolver — it cannot be overwritten.
- [x] 5.6 Count files dropped by `SkipAll` and `OverwriteIfNewer` and report the total once the walk
      finishes, so a skewed clock is visible rather than silent.
- [x] 5.7 Implement the real resolver in `mRemoteNG/UI/Controls/FileTransfer/` over
      `CTaskDialog.ShowTaskDialogBox` command buttons plus `CTaskDialog.CommandButtonResult`, following
      the existing usage in `Runtime` and `DialogFactory`. Show both files' size and modified time.
- [x] 5.8 Marshal the resolver call to the UI thread from `FileManagerTab`, since the walk runs on a
      background task.
- [x] 5.9 Add the prompt's strings to the language resources alongside the other file manager strings.

## 6. Queue and tab wiring

- [x] 6.1 Add an `AllCancelled` event to `TransferQueue`, raised by `CancelAll`, so a walk in progress
      can be stopped rather than refilling a queue the user just emptied.
- [x] 6.2 In `FileManagerTab`, hold a `CancellationTokenSource` for running walks; cancel and replace it
      on `AllCancelled`, and cancel it in `Dispose`.
- [x] 6.3 Rewrite `QueueTransfer` so a directory entry starts a background walk that enqueues each
      `TransferPlanItem` as it arrives, and remove the "not supported yet" message. Route directly
      selected files through the same expansion path so they get the same conflict handling.
- [x] 6.4 Subscribe to the expander's `Skipped` event and report each message through
      `Runtime.MessageCollector` as an information or warning message, matching the surrounding style.
- [x] 6.5 Update `DescribeLocalPaths` so a dropped folder is described as a real directory entry rather
      than one marked to be reported as unsupported.
- [x] 6.6 Report a failure of the walk itself (as opposed to one branch) without tearing down the tab.

## 7. Tests

- [x] 7.1 Add a fake `IFileSystemBrowser` to `mRemoteNGTests/FileTransfer/` that serves a scripted tree,
      records `EnsureDirectoryAsync` calls and their return values, and can be told to throw for named
      paths.
- [x] 7.2 Add a fake `ITransferConflictResolver` that records how many times it was asked and returns a
      scripted answer.
- [x] 7.3 Test a nested tree: every file is yielded once, and each destination path mirrors its position
      relative to the selected directory.
- [x] 7.4 Test that destination separators come from the destination browser, using a fake pair with
      different separators — the case design D2 exists to prevent.
- [x] 7.5 Test that an empty source directory still produces an `EnsureDirectoryAsync` call.
- [x] 7.6 Test that a directory whose creation fails yields nothing beneath it, raises `Skipped`, and
      does not stop sibling branches.
- [x] 7.7 Test that a directory that cannot be listed raises `Skipped` and leaves siblings unaffected.
- [x] 7.8 Test link handling both ways: resolving to a file yields an item, resolving to a directory
      raises `Skipped` and yields nothing, and neither is descended into.
- [x] 7.9 Test that a link cycle terminates, and that the depth limit stops descent and raises `Skipped`
      — driving the limit through the constructor parameter rather than building a 16-deep fake tree.
- [x] 7.10 Test that cancelling the token stops enumeration promptly and yields nothing further.
- [x] 7.11 Test that a transfer with no collisions never asks the resolver, and that a newly created
      destination directory is not listed for collisions.
- [x] 7.12 Test that several collisions ask the resolver exactly once, and that each of `OverwriteAll`,
      `SkipAll` and `Cancel` produces the right set of yielded items.
- [x] 7.13 Test `OverwriteIfNewer` in both outcomes, including that a difference inside
      `TimestampSkewTolerance` counts as not newer, and that a difference just outside it counts as
      newer.
- [x] 7.14 Test that names are compared case-insensitively for a case-insensitive destination and
      case-sensitively for a case-sensitive one.
- [x] 7.15 Test that a file colliding with a directory of the same name is skipped without asking.
- [x] 7.16 Test that a second expansion run asks again rather than reusing the previous answer.
- [x] 7.17 Extend `TransferQueueTests` to cover `AllCancelled` firing on `CancelAll`.
- [x] 7.18 Extend `LocalFileSystemBrowserTests` for `EnsureDirectoryAsync` being safe to call twice and
      reporting correctly whether it created the directory.

## 8. Verification

- [x] 8.1 Build `mRemoteNG/mRemoteNG.csproj` and fix any analyzer warning the new code introduces.
- [x] 8.2 Run the targeted filters `FullyQualifiedName~mRemoteNGTests.FileTransfer` and
      `FullyQualifiedName~mRemoteNGTests.Connection.Sftp` and resolve every failure.
- [x] 8.3 Run the full build and full suite once before finishing, since this change spans two projects
      and widens a shared interface.
- [ ] 8.4 Smoke test against a real server: upload a nested folder, download one back, confirm empty
      directories appear, confirm a link is not followed, confirm the overwrite prompt appears once and
      each of its four answers behaves, and confirm "Cancel all" stops a walk in progress. Record the
      result the way `docs/` records the previous SFTP smoke tests.
