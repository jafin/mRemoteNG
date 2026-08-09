## Why

Deleting a folder that has anything in it fails. Both sides refuse on purpose — `SftpSession.DeleteAsync`
calls `DeleteDirectoryAsync`, which the server rejects for a non-empty directory, and
`LocalFileSystemBrowser` passes `recursive: false` — each with a comment saying the refusal is the point:
the panel reports what the server said rather than quietly recursing through a tree the user never saw.

That reasoning was sound and the outcome is still wrong. The user asked to delete a folder; the file
manager can only delete an empty one, so the operation people expect simply is not available, and there
is no way to do it from the tool at all.

What was actually being avoided was an *invisible* recursive delete: a single click that walks a tree
nobody has looked at and destroys it with no way to watch or stop it. Routing the deletions through the
transfer queue removes that objection rather than overriding it. Every file to be deleted appears as a
queue row, progresses one at a time, and "Cancel all" stops the rest — the same machinery that already
makes a large transfer comprehensible, applied to the operation where it matters more.

## What Changes

- Deleting a directory deletes everything beneath it, on both sides.
- The deletions are queued rather than run in a blocking loop. Each becomes a row in the transfer queue
  with its path and status, so the user can watch what is happening and cancel the remainder.
- Entries are deleted **children first, parent last**, so a directory is only removed once it is empty.
- One confirmation up front, naming the folder and saying plainly that it includes the contents. Not one
  prompt per file — that is a prompt people learn to click through, which is worse than not asking.
- Links are deleted as links and never descended into, so deleting a link does not delete what it points
  at.
- A subdirectory that cannot be listed, or an entry that cannot be deleted, is reported and skipped; the
  rest of the queue continues. A directory whose children could not all be removed will itself fail, and
  is reported rather than forced.
- The queue view gains a delete indicator and leaves the destination column blank, since a deletion has
  no destination.
- `TransferItem` gains an operation kind. **BREAKING** for anything constructing one positionally; it is
  appended with a default so every existing call site is unaffected.

Out of scope: an undo or a recycle bin, deleting across both panes at once, and any change to how a
single file is deleted.

## Capabilities

### New Capabilities

- `recursive-directory-deletion`: deleting a directory and its contents — enumeration order, link
  handling, failure recovery, the confirmation, and how the work appears in the queue.

### Modified Capabilities

None. `sftp-browser-panel`'s queue requirements — per-item and whole-queue cancellation, progress, one
failure not stopping the rest — are what this change relies on and does not alter. No existing
requirement states that a delete is non-recursive; that is an implementation decision recorded in code
comments, which this change replaces along with the behaviour.

## Impact

- `mRemoteNG/FileTransfer/TransferItem.cs` — an operation kind.
- `mRemoteNG/FileTransfer/` — a post-order deletion planner over `IFileSystemBrowser`, alongside the
  existing transfer expander.
- `mRemoteNG/UI/Controls/FileTransfer/FilePaneCommands.cs` — the delete command raises queued work
  instead of deleting inline.
- `mRemoteNG/UI/Controls/FileTransfer/TransferQueueControl.cs` — delete indicator, blank destination.
- `mRemoteNG/UI/Window/FileManagerTab.cs` — runs a queued deletion, and refreshes the affected pane.
- `mRemoteNG/Language/` — the recursive-delete confirmation.
- `mRemoteNGTests/FileTransfer/` — tests for ordering, links, failure recovery and the confirmation.

No new dependencies. `SftpSession.DeleteAsync` and `LocalFileSystemBrowser.DeleteAsync` keep their
current single-entry behaviour: the recursion is built above them, so the primitive that refuses to
delete a non-empty directory stays as the backstop it was written to be.
