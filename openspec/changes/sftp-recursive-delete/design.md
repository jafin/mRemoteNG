# Design

## Context

Both browsers refuse to delete a non-empty directory, deliberately. `SftpSession.DeleteAsync` calls
`DeleteDirectoryAsync` and lets the server refuse; `LocalFileSystemBrowser.DeleteAsync` passes
`recursive: false`. Both carry a comment saying the refusal is the point — "deleting a tree the user has
not seen is not something a delete button should do silently".

The objection in those comments is to *silence*, not to recursion. `sftp-recursive-transfers` has since
built the machinery that answers it: a queue that runs one item at a time, shows every item, cancels
individually or wholesale, and keeps going when one item fails. A tree walker over `IFileSystemBrowser`
already exists in `DirectoryTransferExpander`, along with the rules for links, depth and partial failure.

So this change is mostly assembly. What it must get right is ordering, and what it must not get wrong is
the blast radius.

## Goals / Non-Goals

**Goals:**

- Delete a directory and its contents, on both sides, from the operation the user already reaches for.
- Make the work visible and stoppable while it runs.
- Keep the existing primitives honest: the layer that refuses to delete a non-empty directory stays.
- Confirm once, meaningfully.

**Non-Goals:**

- Undo, a recycle bin, or moving to a trash directory. SFTP has no such concept, and pretending
  otherwise would be a lie about recoverability.
- Deleting the same tree on both sides at once.
- Any change to deleting a single file.
- Parallel deletion. One at a time is what makes cancellation mean something.

## Decisions

### D1 — Deletions become queue items

`TransferQueue` is already an operation queue in everything but name: it takes a `TransferOperation`
delegate and runs whatever the item describes, one at a time, with cancellation and per-item failure
handling. `FileManagerTab.RunTransferAsync` is the delegate, and it branches on what the item is.

So a deletion is a `TransferItem` with an operation kind, and `RunTransferAsync` gains one branch. No new
queue, no second progress model, no second cancellation story — which is the entire reason this feature
becomes acceptable rather than reckless.

Considered instead: a separate deletion queue with its own pane. Rejected — two queues means two places
to look for what the file manager is doing, and the user's own framing was to put deletes in the pane
that already exists.

`TransferItem` gains `TransferOperationKind` (`Transfer`, `Delete`), appended with a default so every
existing construction site compiles unchanged. `Direction` is left alone rather than gaining a `Delete`
member: a deletion has no direction, and overloading the field that drives the ↑/↓ glyph would make the
queue's own display logic lie.

### D2 — Post-order enumeration, in its own planner

A directory can only be removed once it is empty, so children must be queued before their parent. The
queue is FIFO and single-threaded, so enqueueing in post-order is the whole of the ordering guarantee —
no dependency tracking, no retry, no second pass.

`DirectoryDeletionPlanner` walks `IFileSystemBrowser` and yields entries deepest-first: for each
directory, its files, then each subdirectory's contents recursively, then the subdirectory itself, and
finally the directory the user selected.

Not reusing `DirectoryTransferExpander`: it is pre-order by necessity (a destination directory must exist
before what goes in it), it takes a destination browser, and it carries conflict resolution. A deletion
has no destination and the opposite ordering. Sharing them would mean a class parameterised by two
orderings and an optional half of its constructor, which is harder to read than two small walkers that
each do one thing.

Streaming still works: a branch's items are yielded as the walk unwinds, so the queue fills and the first
deletions start while the rest of the tree is still being read.

### D3 — One confirmation, before enumerating

The prompt names the folder and says the contents are included. It is shown *before* the walk, not after
counting, because counting a large remote tree is minutes of round trips and a dialog that appears
minutes after a click has lost the user.

The consequence is that the prompt cannot state an exact number, so it must not pretend to. It says what
is being deleted and that everything in it goes too — which is the fact that matters — and the queue then
shows the actual extent as it fills. Someone who misjudged the folder sees the rows appear and cancels.

`IFilePanePrompts` gains a method for this rather than reusing `ConfirmDelete(count)`, whose message is
about a count of selected items and would read as though only those were going.

### D4 — Links are deleted, never followed

The rule from the transfer work, with more at stake: descending a link when deleting destroys data
outside the selected tree, and that is the one mistake here that cannot be walked back.

A link is yielded as an ordinary entry to delete, and never descended into. Unlike the transfer expander
this needs no following stat to classify — deleting a link deletes the link whatever it points at, so the
question the stat answered does not arise. The depth limit is kept as the same backstop against a cycle
the link check does not anticipate.

### D5 — A parent whose children survived is left alone

If an entry fails to delete, its parent will fail too, because the parent is not empty. That is correct
and is left to happen: the parent's row fails with the server's own message, and the directory stays.

Explicitly *not* forcing it. A child that could not be deleted is usually one that could not be read or
was denied — removing its parent by some other means would destroy exactly the thing the system was just
told it had no business touching.

## Risks / Trade-offs

**A cancelled deletion leaves the tree half-gone, irreversibly** → Inherent: deletion has no undo, and
this is the cost of streaming into a queue rather than doing nothing until everything is known. Post-order
means what survives is a coherent partial tree rather than orphaned children, the queue shows exactly
what was deleted, and cancelling stops the next item rather than interrupting one mid-way. It is stated
in the confirmation, and it is the reason the confirmation exists.

**A misjudged folder is destroyed before the user can react** → The window is real but not instant: the
first item is deleted after the confirmation and the first listing, and "Cancel all" is one click in a
pane already on screen. This is strictly better than the alternative the user has today, which is to
delete the tree by hand from a terminal with no queue at all.

**Queueing a huge tree fills the queue with thousands of rows** → Same trade-off the transfer work
accepted and the same shape of fix if it bites — the streaming walk allows backpressure. Memory, not
responsiveness; the queue view already coalesces repaints.

**The confirmation cannot state how many entries will go** → It names the folder and says the contents
are included, which is the true and useful statement. A count would require the walk to finish first,
turning one click into a minutes-long wait before the user is asked anything.

**`TransferItem` grows a kind, and the queue is no longer only transfers** → The name becomes slightly
wrong before the code does. Renaming the type and its queue is a mechanical change across the file
manager and is deliberately not bundled into a behaviour change.

## Open Questions

- Should a deletion row show progress within a large file, or only a status? Only a status here — a
  delete is atomic from the client's point of view, and a progress bar that jumps from nothing to
  complete is noise.
- Should the confirmation offer "delete contents" versus "delete only if empty"? Left out: the second is
  what the button already did, and nobody asked for it as a choice.
