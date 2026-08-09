## 1. Queue model

- [x] 1.1 Add `TransferOperationKind` (`Transfer`, `Delete`) and append it to `TransferItem` with a
      default of `Transfer`, so every existing construction site compiles unchanged.
- [x] 1.2 Leave `Direction` alone — a deletion has no direction, and overloading the field that drives
      the ↑/↓ glyph would make the queue's display lie. Add a constructor or factory for a deletion item
      that takes the path and leaves the destination empty.
- [x] 1.3 Update `TransferQueueControl` to show a delete indicator in the direction column and nothing in
      the destination column for a deletion.

## 2. The deletion planner

- [x] 2.1 Add `DirectoryDeletionPlanner` in `mRemoteNG/FileTransfer/`, taking a browser and a maximum
      depth defaulting to `DirectoryTransferExpander.DefaultMaximumDepth`, exposing
      `IAsyncEnumerable<FileSystemEntry> PlanAsync(FileSystemEntry root, CancellationToken)` and a
      `Skipped` event.
- [x] 2.2 Yield **post-order**: a directory's files, then each subdirectory's contents recursively, then
      the subdirectory, and finally the selected directory itself. This ordering is the whole of the
      "children before parents" guarantee, because the queue is FIFO and single-threaded.
- [x] 2.3 Yield a link as an entry to delete and never descend into it. No following stat is needed —
      deleting a link deletes the link whatever it points at.
- [x] 2.4 Stop descending at the depth limit and raise `Skipped`, as the backstop against a cycle the
      link rule does not catch.
- [x] 2.5 On a directory that cannot be listed, raise `Skipped` and continue with the remaining branches;
      do not yield that directory itself, since it will not be empty.
- [x] 2.6 Honour the cancellation token at every listing and between yields.

## 3. Confirmation and command

- [x] 3.1 Add a recursive-delete confirmation to `IFilePanePrompts`, taking what is being deleted, and
      implement it on `FilePanePrompts`. Do not reuse `ConfirmDelete(count)`, whose message is about a
      count of selected items and would read as though only those were going.
- [x] 3.2 Add the confirmation string to the language resources, naming the folder and stating plainly
      that the contents go too and that it cannot be undone.
- [x] 3.3 Ask once, before enumerating — counting a large remote tree is minutes of round trips, and a
      dialog that appears minutes after the click has lost the user. Ask only when the selection contains
      a directory; a file-only selection keeps today's prompt.
- [x] 3.4 On decline, delete nothing and queue nothing.
- [x] 3.5 Change `FilePaneCommands.DeleteAsync` to raise the planned deletions rather than deleting
      inline, leaving the single-file path behaving as it does today.

## 4. Running the deletion

- [x] 4.1 In `FileManagerTab`, walk the planner in the background and enqueue each entry as it arrives,
      reusing the expansion cancellation source so "Cancel all" and closing the tab both stop it.
- [x] 4.2 Branch `RunTransferAsync` on the operation kind: a deletion calls the owning browser's
      `DeleteAsync` for that entry.
- [x] 4.3 Refresh the affected pane when the queue drains, rather than after each item, so deleting a
      thousand files does not re-list a thousand times.
- [x] 4.4 Report `Skipped` messages through `Runtime.MessageCollector`, matching the surrounding style.
- [x] 4.5 Leave a parent whose children survived to fail on its own with the server's message. Do not
      force it — a child that could not be deleted was usually one that could not be read, and removing
      its parent by other means destroys what the system was told it could not touch.

## 5. Tests

- [x] 5.1 Test post-order on a nested tree: every entry appears exactly once, and each directory after
      everything beneath it.
- [x] 5.2 Test that the selected directory itself is yielded last.
- [x] 5.3 Test an empty directory yields just itself, and a single file yields just itself.
- [x] 5.4 Test that a link is yielded and not descended into, including a link pointing at an ancestor,
      and that enumeration terminates.
- [x] 5.5 Test that a directory that cannot be listed raises `Skipped`, is not itself yielded, and does
      not stop sibling branches.
- [x] 5.6 Test the depth limit stops descent and raises `Skipped`, driving the limit through the
      constructor rather than building a deep fake tree.
- [x] 5.7 Test that cancelling the token stops enumeration and yields nothing further.
- [x] 5.8 Extend `FilePaneCommandsTests` for the confirmation: declining deletes nothing and queues
      nothing, and a directory selection asks the recursive question rather than the count one.
- [x] 5.9 Test that a deletion `TransferItem` carries the delete kind and an empty destination.

## 6. Verification

- [x] 6.1 Compile `mRemoteNG/mRemoteNG.csproj`, building to a temp `OutputPath` if `bin\` is locked by a
      running mRemoteNG.exe.
- [x] 6.2 Run `FullyQualifiedName~mRemoteNGTests.FileTransfer` and resolve every failure.
- [x] 6.3 Run the full build and full suite before finishing, since this touches the queue model that
      every transfer already uses.
- [ ] 6.4 Smoke test against a real server: delete a nested folder and confirm the rows appear
      children-first and the folder goes last; cancel a deletion part-way and confirm it stops and the
      remaining entries survive; delete a folder containing a link and confirm the link's target is
      untouched; decline the confirmation and confirm nothing is queued. Record the result the way
      `docs/` records the previous SFTP smoke tests.
