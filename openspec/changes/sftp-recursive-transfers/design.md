# Design

## Context

`add-sftp-browser-panel` shipped the dual-pane file manager and listed "recursive directory transfer"
as an explicit non-goal. The limitation is visible in one place — `FileManagerTab.QueueTransfer`
reports `Skipped <name>: transferring a whole directory is not supported yet` and drops the entry —
and in `DescribeLocalPaths`, which deliberately marks a dropped folder as a directory so it lands on
that same branch.

Everything needed to lift the limitation is already there and already asynchronous. `IFileSystemBrowser`
is implemented by both sides and exposes `ListAsync`, `Combine`, `GetParentPath`, `OpenReadAsync`,
`WriteAsync` and `CreateDirectoryAsync`. `TransferQueue` already runs items one at a time in the
background with per-item and whole-queue cancellation. Underneath, SSH.NET 2025.1.0 gives the remote
side `ExistsAsync` and `GetAttributesAsync` (a following stat, unlike the lstat behind a listing).

What is missing is the step in between: turning one selected directory into the set of files to move
and the set of directories that must exist first.

## Goals / Non-Goals

**Goals:**

- One expansion implementation serving both directions, testable without a server.
- Expansion off the UI thread, feeding the queue as it goes rather than in one batch at the end.
- Bounded work on hostile input: a link cycle must not fill the queue until memory runs out.
- Partial failure stays partial — one unreadable subdirectory does not lose the other branches.
- No change to how a single-file transfer behaves or to the queue's contract.

**Non-Goals:**

- Recursive delete. Same shape of problem, different blast radius; it deserves its own change.
- Preserving mode bits, ownership or timestamps on the copy.
- Resuming an interrupted tree.
- Per-file overwrite prompting, or remembering an overwrite choice across separate transfers.
- Parallel transfers. `TransferQueue` runs one at a time for reasons that this change does not alter.
- Server-to-server transfer.

## Decisions

### D1 — One expander over `IFileSystemBrowser`, not one per side

Expansion is written once, against the interface both panes already implement, and is parameterised by
a source browser and a destination browser. Upload is `(local, remote)`; download is `(remote, local)`.

The alternative — recursive listing on `ISftpSession` plus a separate local walk — was rejected because
it would duplicate the interesting logic (depth limits, link handling, error recovery, relative path
tracking) in two places and put half of it behind a type that needs a server to exercise. With one
expander over the interface, every scenario in the spec is a unit test against a fake browser, which is
the same bargain the rest of this feature already made.

### D2 — Relative segments, not path strings

The expander tracks position in the tree as a list of name segments and hands each one to the
*destination* browser's own `Combine`. It never concatenates a source path with a destination path.

This is not fussiness. The two sides use different separators, and the only reason the existing
single-file path works is that it joins one name onto the destination's current directory. Building
`remoteDir + "/" + @"sub\file.txt"` would produce a remote file literally named `sub\file.txt` — a
legal name on a unix server, so nothing would fail; the user would simply get a flat directory of
oddly-named files. Keeping segments separate makes that class of bug unrepresentable.

### D3 — Destination directories are created during the walk, pre-order

The expander creates each destination directory as it descends, before yielding any of the files
inside it.

Considered instead: creating directories lazily inside `RunTransferAsync`, just before each file is
written, with a cache of what has already been ensured. Rejected on three counts — it needs a cache
whose lifetime and thread-safety are a new question in the queue runner; it leaves an empty source
directory with no mechanism at all, so empty directories would need queue items of their own; and it
discovers "this whole branch cannot be created" only after several thousand doomed items are already in
the queue.

Creating pre-order makes ordering correct by construction, handles empty directories for free, and
surfaces a permission problem at the top of the branch rather than once per file beneath it. The cost
is that cancelling a queue can leave empty directories behind at the destination. That is a fair trade:
the user asked for the tree, and an empty directory is not damage.

`CreateDirectoryAsync` is not idempotent on the remote side, so `IFileSystemBrowser` gains
`EnsureDirectoryAsync`, returning whether it had to create the directory. Local delegates to
`Directory.CreateDirectory`, which is already idempotent; remote checks `SftpClient.ExistsAsync` first
and creates only when absent. The check-then-create race is accepted — losing it means the create fails
with "already exists", which is the state we wanted. The return value is what D7 uses to decide whether
a directory can contain collisions at all.

### D4 — Links are resolved once, and never descended into

A symbolic link that points at one of its own ancestors makes the tree infinite. The rule is therefore
absolute: **the expander never descends into a link**, on either side.

A link still has to be classified, because a link to a file should transfer like any other file. A
listing cannot answer that: SFTP's listing is an lstat, so a link to a directory arrives with
`IsSymbolicLink = true` and `IsDirectory = false` and is indistinguishable from a link to a file. So
each link — and only a link, which is rare — costs one following stat: `GetAttributesAsync` remotely,
`Directory.Exists` locally, which follows a reparse point on Windows. A link resolving to a file is
queued as a file; a link resolving to a directory is reported and skipped.

Carrying this needs `FileSystemEntry.IsSymbolicLink`, which the model does not have today. `SftpEntry`
already carries it and `RemoteFileSystemBrowser.Describe` currently drops it; locally it is
`FileAttributes.ReparsePoint`. The new member is appended to the positional record with a default of
`false`, so existing construction sites keep compiling.

A depth limit backs all of this up, as a named constant `DefaultMaximumDepth = 16`. It is not the
primary defence — the link rule above is — but it is what stops any cycle the link check does not
anticipate.

Sixteen is a deliberate "start conservative" choice rather than a measured one, and unlike the link
rule it is a limit real trees can reach: a deeply nested `node_modules`, or a Java package tree under
`src/main/java/...`, gets there without being pathological. That is why hitting the limit is reported
rather than silent (see the spec's depth scenario) — a user who sees "not expanded in full" knows to
transfer the subtree directly. The constant is written to be raised, and making it a setting is
deferred rather than dismissed.

### D5 — Expansion streams into the queue

`ExpandAsync` returns `IAsyncEnumerable<TransferPlanItem>`; `FileManagerTab` enqueues each item as it
arrives, on a background task, with its own `CancellationTokenSource`.

Streaming rather than returning a list matters at both ends of the size range. On a large remote tree
the walk is minutes of round-trips, and a queue that stayed empty for all of it would look broken while
the transfer had in fact started. It also means the first file begins moving while the rest of the tree
is still being discovered, since `TransferQueue` starts its runner on the first `Enqueue`.

The tab cancels the walk when it closes, and when the user cancels the whole queue. The queue has no
way to signal the latter today, so `TransferQueue` gains an `AllCancelled` event raised by `CancelAll`.
Without it, "Cancel all" would empty the queue and then watch it refill from a walk still in progress.

### D6 — Expanded items are ordinary `TransferItem`s

The expander produces exactly the items `QueueTransfer` produces today, and `RunTransferAsync` is
unchanged. Nothing downstream of the queue can tell how an item got there.

The alternative — a "directory transfer" item that owns its own children — was rejected. It would need
its own progress aggregation, its own cancellation semantics and its own failure rules, all of which
the queue already has, and it would make "cancel this one file" either impossible or a special case.

### D7 — Collisions are detected per directory, and answered once per transfer

Today a transfer replaces whatever is at the destination without saying so. That is tolerable for one
file the user just picked; for a tree of several thousand it is a way to destroy work silently. The
resolution is a single question — overwrite all, skip all, overwrite if newer, cancel — asked on the
**first** collision and applied to the rest of that transfer.

Asked lazily, not up front: a transfer with no collisions must not produce a dialog, and whether there
are any is not known until the walk finds one. Asked once, not per file: a hundred prompts is a hundred
chances to click the wrong one, and it is the reason per-file prompting is an explicit non-goal.

**Detection costs one listing per directory, not one stat per file.** D3 already creates each
destination directory pre-order and now reports whether it had to create it. A directory it created is
empty, so nothing beneath it can collide and no check is needed at all. A directory that already existed
is listed once, and that listing answers every collision question for the files in it — name, kind and
timestamp — for free. The obvious alternative, a stat per file, would have doubled the round trips on
the exact workload this change exists to serve: a ten-thousand-file tree would pay ten thousand extra
round trips to discover that the destination was empty.

Name comparison follows the destination filesystem, so `IFileSystemBrowser` gains
`PathsAreCaseSensitive` — the same fact `FileManagerTab` already passes to `FilePaneController` by hand
as `caseSensitivePaths`, moved onto the browser where it belongs. Without it, uploading `Readme.md` onto
a server that already has `README.md` would look like two different files, and downloading the reverse
would silently overwrite.

Skipped files are **not** queued. A queue row that will never run is the same mistake as queueing a
directory that cannot succeed, which the current code already avoids; the count is reported instead.
Adding a `Skipped` status to `TransferStatus` was considered and rejected for that reason — it would put
rows in the queue that the runner must then learn to ignore.

"Newer" compares last-write times through a named skew tolerance, `TimestampSkewTolerance =
TimeSpan.FromSeconds(2)`: the source counts as newer only when it exceeds the destination by more than
that. The tolerance earns its place twice over. FAT timestamps have two-second granularity, so an exact
comparison would call a file newer than its own identical copy; and client and server clocks are never
exactly aligned, so a sub-second difference says nothing about which file is actually more recent.

What it does *not* do is rescue a genuinely skewed clock — a server minutes ahead defeats any tolerance
small enough to be useful, and that stays in the risks below. Two seconds is the same window rsync uses
by default, chosen for the same reasons, and it is a constant precisely so it can become a setting when
someone hits a case that needs a different one.

The prompt goes behind `ITransferConflictResolver`, exactly as `IFilePanePrompts` already puts dialogs
behind an interface so the interesting cases stay testable: that skip-all queues nothing, that the
answer is reused, and that a clean transfer never asks. The real implementation uses the in-repo
`CTaskDialog` command buttons — the same mechanism `Runtime` and `DialogFactory` already use — so four
named choices need no new dialog code. The expander runs on a background task, so `FileManagerTab`
marshals the call to the UI thread.

## Risks / Trade-offs

**A tree with tens of thousands of files fills the queue with tens of thousands of rows** → The queue
holds `TransferItem` objects and `TransferQueueControl` already coalesces repaints on a 250 ms timer, so
the cost is memory rather than responsiveness. Accepted for this change and left measurable; if it bites,
the fix is a cap on queued items with backpressure on the walk, which D5's streaming shape already allows.

**Cancelling mid-tree leaves a partial copy, and possibly empty directories** → Inherent to a queue of
independent items, and consistent with what cancelling a multi-file selection already does. The queue
shows exactly which items completed.

**A link that resolves to a directory is silently not copied** → It is reported, not silent. Copying the
target would mean duplicating shared data and reintroducing the cycle problem; leaving it is what every
comparable tool does by default.

**The extra stat per link adds a round-trip** → Only for links, and only once each. A tree of ordinary
files pays nothing.

**`EnsureDirectoryAsync` widens `IFileSystemBrowser`** → Two implementations in-repo, both updated here.
The interface is internal to the file manager and has no external implementers.

**"Overwrite if newer" is only as good as the two clocks** → A server running minutes ahead of the
client makes every remote file look newer, so a download copies nothing and an upload copies everything.
The two-second tolerance absorbs filesystem granularity and sub-second drift; it does nothing for skew
of that size, and no tolerance small enough to be useful would. This is inherent to comparing timestamps
across two machines — rsync has the same caveat — and the mitigation is that the option is one of four
rather than the default: overwrite-all and skip-all are both unaffected by clock skew, and the report
says how many files were skipped, which is what makes a skewed clock visible rather than silent.

**The depth limit is reachable by real trees** → Sixteen levels is not pathological; a nested
`node_modules` or a deep package tree can exceed it. Reaching it is reported, not silent, so the outcome
is a visible partial transfer rather than quiet data loss — but it is the constant most likely to need
raising, which is why both it and the tolerance are named constants in one place.

**A collision check reads a directory listing that may be large** → One listing per pre-existing
destination directory, however few files are going into it. A destination directory with a hundred
thousand entries is read in full to transfer three files. Bounded, one round trip, and far cheaper than
the per-file stat it replaces.

**A file can appear at the destination between the check and the write** → The window is real and
unavoidable without server-side atomic create-exclusive, which SFTP does not usefully offer here. Losing
the race means the file is overwritten — the same outcome the file manager has today, so the check
strictly improves on the current behaviour rather than promising something it cannot keep.

## Migration Plan

None required. No persisted format, setting or connection property changes. The feature replaces a
message that said the operation was unsupported, so there is no prior behaviour to migrate from and
nothing to roll back beyond reverting the change.

## Open Questions

- `DefaultMaximumDepth` (16) and `TimestampSkewTolerance` (2 seconds) are hard-coded for this change and
  intended to become settings later. Both are named constants declared together on the expander, so
  promoting them is a mechanical change: the values are not spread through the walk, and neither is
  baked into the spec, which says "a maximum expansion depth" and "a small tolerance" rather than naming
  a number.
- Should the overwrite choice be remembered for the tab's lifetime rather than one transfer? Answered
  "one transfer" here, because a remembered "overwrite all" is invisible at the moment it matters. If
  users find repeated prompting tedious across many small drags, a "remember for this session" checkbox
  is the natural next step — `CTaskDialog` already supports one.
