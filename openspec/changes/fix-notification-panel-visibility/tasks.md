# Tasks

## 1. Backlog delivery (the silent-loss defect)

- [x] 1.1 Add `MessageCollector.Messages` snapshot semantics — return a copy taken under the existing lock instead of the live backing list.
- [x] 1.2 Add an operation that subscribes a handler and hands it the existing backlog as one step, so no message can slip between the two. Subscribe and snapshot happen under one lock; the backlog is delivered outside it, because writers marshal to the UI thread and holding the lock across that would deadlock against a background thread waiting to add. That leaves a theoretical double-delivery window needing a concurrent writer — documented on the method, and impossible for the only caller, which runs during form load before background work starts.
- [x] 1.3 Use it from `MessageCollectorSetup.SetupMessageCollector`; attach writers before the backlog is delivered so the delivery reaches them.
- [x] 1.4 Reorder `FrmMain_Load` so writers are built before the collector is wired, and drop the manual re-post of the startup timing message that worked around the old ordering.
- [x] 1.5 Tests: backlog delivered in order; delivered exactly once; empty collector delivers nothing; later messages still delivered; concurrent read during writes does not throw.

## 2. Panel filter defaults

- [x] 2.1 Flip `NotificationPanelWriterWriteInfoMsgs` and `NotificationPanelWriterWriteWarningMsgs` to `True` in `OptionsNotificationsPage.settings` and the generated designer.
- [x] 2.2 Test the shipped defaults, reading the declared default rather than the current user value.
- [x] 2.3 Confirm `OnlyLog` still keeps messages out of the panel regardless of filters.

## 3. Timestamps

- [x] 3.1 Add a timestamp column to `ErrorAndInfoWindow` and size it with the existing resize logic.
- [x] 3.2 Populate it in `NotificationMessageListViewItem` from `IMessage.Date`, not from render time. The timestamp became the item's `Text` and the message moved to a subitem, so the panel's search box had to stop filtering on `ListViewItem.Text` — otherwise searching "10" would match every message logged in the tenth minute of an hour. Filtering now goes through `MatchesFilter`.
- [x] 3.3 Add the column header to the language resources.
- [x] 3.4 Tests: the item carries the message's own timestamp.

## 4. Follow-through on the credential diagnostics

- [x] 4.1 Make the SSH agent "too many identities" warning panel-visible — it is actionable and was marked log-only.
- [x] 4.2 Review the other messages added by `add-ssh-agent-credential-resolver` and confirm each is on the right side of `OnlyLog`.

## 5. Completion

**Results 2026-08-08:** full build green (70.9s); full suite **6914/6914**, 133s, 0 crashes; 38 new tests. The only warning in the touched files, MA0158 on `MessageCollector`'s lock object, predates this change and was left alone rather than swapping a concurrency primitive as a side effect.


- [x] 5.1 Full build.
- [x] 5.2 Full test suite; zero failures, no `[Ignore]`.
- [x] 5.3 Zero new analyzer warnings.
- [x] 5.4 `openspec validate fix-notification-panel-visibility --strict`.
- [ ] 5.5 Manual check: start the app and confirm startup messages appear with timestamps, including any settings-load failure. **Outstanding** — needs the GUI; cannot be claimed from a green suite.
