# Tasks

## 1. One ordering point

- [x] 1.1 Add `NotificationMessageQueue`: a lock-guarded FIFO of `IMessage`, replacing `_pendingItems` and `_flushPosted`.
- [x] 1.2 Make enqueuing and asking for a drain one atomic step, so exactly one drain is ever outstanding.
- [x] 1.3 Make emptying the queue and giving up the drain one atomic step, so a message queued at that moment is not left with nothing scheduled to collect it.
- [x] 1.4 Give it a way to release a drain that could not be posted, so the messages are not stranded when the handle disappears between scheduling and posting.

## 2. Route every message through it

- [x] 2.1 `Write` enqueues and returns. No message goes straight to the panel, including the UI thread's own — that direct path is what let a later entry overtake an earlier one.
- [x] 2.2 Build `ListViewItem`s during the drain, on the UI thread, rather than on whichever thread reported the message.
- [x] 2.3 Keep the handle-created path: no handle, no post, messages wait. Keep posting the drain rather than running it in the handler, because `ListView` raises `HandleCreated` before pushing its columns and an item added there renders its timestamp only.
- [x] 2.4 Re-test the handle after subscribing: it can appear between the two, and then nothing raises the event.
- [x] 2.5 Discard the queue when the panel is disposed.

## 3. Tests

- [x] 3.1 Ordering: messages come out in the order they went in; one queued mid-drain keeps its place at the back.
- [x] 3.2 Scheduling: the first message asks for a drain and later ones do not; emptying releases it; a message queued before the drain ends is collected by it.
- [x] 3.3 Concurrency: concurrent reporters lose nothing; exactly one is told to schedule; nothing is delivered twice under concurrent draining.

## 4. Verification

- [x] 4.1 Full build; zero new analyzer warnings.
- [x] 4.2 Full test suite; zero failures.
- [x] 4.3 `openspec validate fix-notification-panel-delivery --strict`.
- [x] 4.4 Manual: start the application with the panel auto-hidden, let startup messages accumulate, then open the panel. Every message is there, newest at the top, timestamps ascending downwards.
- [x] 4.5 Manual: trigger a background report (a failing connection) at the same time as a UI one (an options change) and confirm the panel order matches the timestamps.

Both confirmed on 2026-08-10, in the same isolated `Release Portable` sandbox used for
[fix-connection-save-durability](../fix-connection-save-durability/tasks.md) — see the note there on
why a real profile was not used.

4.4 is the case the queue exists for and the one that could not be covered by a test: messages
reported before the panel has a window to render into, held, and then drained in a single pass once
it does. Every message present with the newest at the top means the drain ran in enqueue order
rather than inserting the buffered ones above the live ones.

## Notes

The ordering weakness predates the `Invoke` → `BeginInvoke` change in
`fix-connection-save-durability` that prompted the review comment: `Invoke` blocks the reporting
thread, not the UI thread, so it never had the chance to insert an earlier message ahead of a later
direct one either. What that change altered is how wide the window is, not whether it exists.

The data race is the more serious half and was not raised in review — it was found while checking
whether the ordering claim held.
