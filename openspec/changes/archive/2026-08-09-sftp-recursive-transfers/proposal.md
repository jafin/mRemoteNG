## Why

The file manager can only move individual files. Selecting a folder — or dragging one in from Explorer
— produces "Skipped `<name>`: transferring a whole directory is not supported yet", so the one thing
people most often want to move (a source tree, a config directory, a set of logs) has to be done a
file at a time with the destination folders created by hand. This is the largest remaining gap between
the new file manager and the tools it is meant to replace.

## What Changes

- Selecting a directory and transferring it queues every file beneath it, in both directions
  (upload and download), instead of reporting the directory as unsupported.
- Dragging a folder from Explorer onto the remote pane uploads its whole tree.
- Missing destination directories are created automatically, including directories that are empty in
  the source, so the destination tree mirrors the source tree.
- When a transfer would replace files that already exist at the destination, the user is asked once —
  overwrite all, skip all, overwrite only where the source is newer, or cancel — and the answer is
  applied to every collision in that transfer without asking again. This replaces today's behaviour of
  silently overwriting, and applies to any transfer with a collision, not only a recursive one, so a
  multi-file selection and a folder behave the same way.
- Directory trees are walked in the background and items appear in the queue as they are discovered,
  so a large tree does not freeze the tab before the first byte moves.
- Symbolic links and Windows reparse points are never descended into. A link resolving to a file is
  transferred like any other file; one resolving to a directory is reported and skipped. This is what
  stops a link pointing at an ancestor from expanding forever, and a depth limit backs it up.
- A subdirectory that cannot be listed (permission denied, vanished mid-walk) is reported and skipped;
  the rest of the tree still transfers.
- Expansion is cancellable, and cancelling the queue also stops a walk that is still running.
- `FileSystemEntry` gains an `IsSymbolicLink` flag, populated from the SFTP attribute on the remote
  side and from `FileAttributes.ReparsePoint` on the local side. It is appended to the record with a
  default, so existing construction sites are unaffected.

Explicitly out of scope: recursive **delete**, preserving permissions or timestamps on the copy,
resuming an interrupted tree, per-file overwrite prompting, and transferring more than one file at a
time. Each is a separate concern with its own risks, and none of them is needed to make a folder
transfer work.

## Capabilities

### New Capabilities

- `recursive-directory-transfers`: transferring a directory and everything beneath it between the
  local and remote panes — tree expansion, destination directory creation, link and failure handling,
  and how the resulting items appear in the transfer queue.

### Modified Capabilities

None. The behaviour this change replaces — directories being skipped — is a limitation of the current
implementation, not a requirement written down in `sftp-browser-panel`, so no existing requirement
changes. The queue, progress and cancellation requirements already in that capability continue to hold
unchanged: a recursive transfer produces ordinary queue items and is subject to exactly the same rules.

## Impact

Affected code:

- `mRemoteNG/FileTransfer/` — new tree expander and transfer-plan model; `FileSystemEntry` gains
  `IsSymbolicLink`; `IFileSystemBrowser` gains idempotent directory creation, link resolution and a
  case-sensitivity flag, with both browsers implementing them; a conflict-resolution prompt behind an
  interface, following the existing `IFilePanePrompts` pattern; `TransferQueue` gains an event so a
  walk can be stopped by "Cancel all".
- `mRemoteNG/UI/Controls/FileTransfer/` — the real prompt, over the existing in-repo `CTaskDialog`
  command buttons already used by `Runtime` and `DialogFactory`. No new dialog machinery.
- `mRemoteNG/Connection/Sftp/` — `ISftpSession` gains existence and following-stat operations, backed
  by SSH.NET's `ExistsAsync` and `GetAttributesAsync`.
- `mRemoteNG/UI/Window/FileManagerTab.cs` — `QueueTransfer` and `DescribeLocalPaths` expand
  directories instead of reporting them as unsupported; the running walk is cancelled on close.
- `mRemoteNGTests/FileTransfer/` — tests for the expander against a fake browser, covering nesting,
  empty directories, links, depth limits, unreadable subdirectories and cancellation.

No new dependencies. No configuration or connection-file format changes. Nothing outside the file
manager is touched, and single-file transfers keep their current code path and behaviour.
