## 1. Reconnectable session

- [x] 1.1 Extract the client teardown in `SftpSession` into a private method that unsubscribes
      `ErrorOccurred` **before** disposing, so a dying client cannot raise `Dropped` against its
      successor.
- [x] 1.2 Call it at the top of `ConnectAsync`, and dispose the previous `_authentication` there too,
      making a second connect safe. Leave `_credential` alone — it is read lazily on every connect and
      must outlive them all.
- [x] 1.3 ~~Add `SftpSessionTests` coverage that connecting twice disposes the first client and leaves
      no subscription behind, using the existing fakes rather than a server.~~ **Not unit-testable.**
      `ConnectAsync` builds a real `SftpClient` and opens a socket before any of this is reachable, and
      `SftpClient` is a concrete SSH.NET class with no seam to fake — the repository's rule that no test
      may require a server is what rules it out. Verified instead by the reconnect leg of the smoke test
      (7.4), which is where a leaked client or a stale `Dropped` subscription would actually show.

## 2. Connection contract for the pane

- [x] 2.1 Add `IPaneConnection` to `mRemoteNG/FileTransfer/` with `IsConnected`, `Task<bool>
      ReconnectAsync(CancellationToken)` and `event EventHandler? ConnectionChanged`.
- [x] 2.2 Implement it on `FileManagerTab`, reusing the existing connect path so credential diagnostics
      are reported the same way on a reconnect as on the first connect.
- [x] 2.3 Guard it single-flight under the tab's lock: a second caller returns `false` immediately
      rather than queueing behind the first.
- [x] 2.4 On success, re-list the remote pane and restore the connected tab title; on failure, report
      the reason through `Runtime.MessageCollector` and stay disconnected.
- [x] 2.5 Raise `ConnectionChanged` from both the drop handler and the end of a reconnect attempt.
- [x] 2.6 Add a `TabPageContextMenuStrip` to the tab with a Reconnect item calling the same guarded
      operation, enabled on the same condition and refreshed when `ConnectionChanged` fires.

## 3. Reconnect in the pane

- [x] 3.1 Give `FilePaneControl` an optional `IPaneConnection` constructor parameter, defaulting to
      null so the local pane and the existing two-argument callers are unaffected.
- [x] 3.2 Add a Reconnect toolbar button, created only when a connection was supplied, labelled from
      the existing `Language.Reconnect`.
- [x] 3.3 Enable it only while disconnected; update it from `ConnectionChanged` and in `Rebind`.
- [x] 3.4 Route Refresh through a handler that reconnects first when disconnected and lists only if
      that succeeded, so a failed reconnect does not produce a second error for one gesture.
- [x] 3.5 Unsubscribe `ConnectionChanged` in `Dispose`.

## 4. Parent navigation row

- [x] 4.1 Append `bool IsParentNavigation = false` to `FileSystemEntry`, documented as the synthetic
      `..` row rather than anything the filesystem reported.
- [x] 4.2 Add `FileSystemEntry? ParentEntry` to `FilePaneController`, set after each successful listing
      and null at the root, where `GetParentPath(path) == path`.
- [x] 4.3 Keep it out of `Entries`, so the item count and size total stay correct without subtracting
      it back out.
- [x] 4.4 Prepend it in `FilePaneControl.Rebind` when present.
- [x] 4.5 Filter it out of `SelectedEntries`, which is what stops transfer, rename and delete seeing it.
- [x] 4.6 Navigate up when it is activated, in `OnItemActivate`.
- [x] 4.7 Pin it above the sort with a `CustomSorter` wrapping `ColumnComparer`, so it stays on top in
      both directions and for every column.

## 5. Columns and formatting

- [x] 5.1 Add a Type column returning `Link` when `IsSymbolicLink`, then `Folder` when `IsDirectory`,
      then `File` — and empty for the parent row.
- [x] 5.2 Build a `SmallImageList` holding `Properties.Resources.FolderClosed_16x` under `"folder"` and
      `Document_16x` under `"file"`, and assign it to the list. The Name column's existing `ImageGetter`
      already returns those keys and starts working unchanged.
- [x] 5.3 Dispose the image list with the control.
- [x] 5.4 Add a tested `AlignedDateTimePattern(CultureInfo)` helper widening single `d`, `M`, `h` and
      `H` in the culture's short date and time patterns, skipping quoted literals.
- [x] 5.5 Format the Modified column through it, and leave the cell empty for the parent row.
- [x] 5.6 Change the Size aspect to yield `null` for directories and the parent row, and make
      `DescribeSize`'s converter pass `null` through as empty instead of coercing it to `0 B`.
- [x] 5.7 Add the Type column values and header to the language resources, alongside the existing file
      manager strings.

## 6. Tests

- [x] 6.1 Test that `ParentEntry` is null at the root and non-null below it, on a fake browser for each
      of the two path syntaxes.
- [x] 6.2 Test that `Entries` never contains the parent row, so the count and total are unaffected.
- [x] 6.3 Test `AlignedDateTimePattern` for en-GB and en-US — padded, and field order preserved — and
      for a pattern containing a quoted literal.
- [x] 6.4 Test `DescribeSize` renders `0 B` for an empty file and empty for a null aspect.
- [x] 6.5 Test the Type value for a directory, a file, a link and the parent row.
- [x] 6.6 Test the parent-first comparer keeps the row on top for both sort directions.
- [x] 6.7 Test the single-flight reconnect guard: a second concurrent call returns false without
      starting a second attempt, and a later call after it finishes is allowed.
- [x] 6.8 Test that refresh reconnects when disconnected, does not when connected, and does not list
      when the reconnect fails — against a fake `IPaneConnection`, with no UI.

## 7. Verification

- [x] 7.1 Compile `mRemoteNG/mRemoteNG.csproj` and fix any analyzer warning the new code introduces.
      If `bin\` is locked by a running mRemoteNG.exe, build to a temp `OutputPath`.
- [x] 7.2 Run `FullyQualifiedName~mRemoteNGTests.FileTransfer` and
      `FullyQualifiedName~mRemoteNGTests.Connection.Sftp`, resolving every failure.
- [x] 7.3 Run the full build and full suite before finishing, since this touches a shared model record
      and a control both panes use.
- [ ] 7.4 Smoke test against a real server: confirm icons and the Type column render, `..` navigates and
      cannot be transferred or deleted, dates align, folders show no size, then drop the connection
      (stop the service or pull the network) and confirm both Reconnect and Refresh recover the session
      and re-list. Reconnect several times in a row and confirm the tab does not accumulate connections
      — this is the leg that stands in for the unit test 1.3 could not be. Record the result the way
      `docs/` records the previous SFTP smoke tests.
