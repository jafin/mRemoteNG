# Tasks

Written up while the evidence was fresh, then implemented. Independent of the mRemoteNG#3416 audit
releases.

Sections 5 and 6 were added during implementation: the debounce flush turned out not to be the whole
cause, and the repro needed all of it.

## 1. Flush on exit

- [x] 1.1 Give `ConnectionsService` a way to complete a pending debounced save synchronously: cancel the timer, and if a save was outstanding, perform it under `SaveLock`.
- [x] 1.2 Call it from `Shutdown` **before** the conditional `SaveConnections`, and unconditionally — `SaveConnectionsFrequency` decides how often to save periodically, not whether the user's last edit survives.
- [x] 1.3 Bound it. A write that cannot complete must not hang shutdown; time-box the flush and report rather than blocking.
- [x] 1.4 Check the same gap on the other exit paths — close to tray, log off, and whatever `Shutdown.Cleanup` is reached by — rather than only the main one.
- [x] 1.5 Tests: a pending save is written when shutdown runs; nothing is written when none is pending; the frequency setting does not suppress it.

On 1.3, the flush writes on the calling thread and time-boxes acquiring the save lock. Handing the
write to a worker and waiting on it would deadlock the UI thread against the worker's own progress
messages marshalling to the notification panel — reliably, on every exit. What remains unbounded is a
write that blocks forever underneath, which is unbounded on every other save path too.

On 1.4, every exit path funnels through `FrmMain.Close()` → `FrmMain_FormClosing` →
`Shutdown.Cleanup`: the File menu and the tray icon both go through `Shutdown.Quit`, and minimise-to-tray
only hides the window. One flush at `Shutdown.SaveConnections` covers them all.

## 2. Say when a save did not happen

- [x] 2.1 Distinguish the four silent paths in `SaveConnections` — no model or file name, file not loaded, batching in progress, and the swallowed exception — and report the ones that mean the user's change was not stored.
- [x] 2.2 Batching is not a failure: it defers to `_saveRequested`. Confirm that flag is always honoured afterwards, because if a batch ends without draining it the change is lost the same way and nothing says so.
- [x] 2.3 Surface failures somewhere visible without opening the log, without a modal dialog per occurrence. Saves are frequent and mostly automatic.
- [x] 2.4 Tests: a throwing save reports; a skipped save reports; a successful save reports nothing.

2.2 found three defects rather than confirming one. `ConnectionTree.SortRecursive` called
`BeginBatchingSaves` with no `try`/`finally`, so an exception left batching on for the rest of the
session and swallowed every later save. `EndBatchingSaves` never cleared `_saveRequested` or
`_saveAsyncRequested`, so the next batch to end saved whether or not anything had asked it to. And
the flag was a bool, so a nested context ending released the outer one. Now a depth counter, drained
and cleared on reaching zero, with `SortRecursive` using the existing `ExecuteInBatchedSaveContext`.

2.3 reports through `MessageCollector` at warning level, which reaches the notification panel and
focuses it. Popups stay opt-in per the user's notification settings, so nothing becomes modal.

## 3. Keep the coalescing

- [x] 3.1 Leave the two-second debounce alone. It exists because each save re-encrypts every password at 600,000 iterations, and removing it restores the problem it was added for.
- [x] 3.2 Test that rapid successive changes still produce one write.

## 4. Verification

- [x] 4.1 Full build; zero new analyzer warnings.
- [x] 4.2 Full test suite; zero failures, no `[Ignore]`. 7274 passed.
- [x] 4.3 `openspec validate fix-connection-save-durability --strict`.
- [ ] 4.4 Manual, the case that found this: set a master password, remove it, save, quit immediately, restart. The password is gone.
- [ ] 4.5 Manual: rename a connection and quit within two seconds. The rename survives.
- [ ] 4.6 Manual: make the connection file read-only, edit something, quit. The failure is visible without opening the log, and shutdown still completes.
- [ ] 4.7 Manual: on a profile that has never opened Tools → Options → Connections, make an edit and quit. It survives.

## 5. Make the edit reach the save in the first place

Found while verifying section 1: the flush cannot write a save nobody requested.

- [x] 5.1 `RootNodeInfo.Password` was `public new bool Password { get; set; }`, hiding the base property that does notify. Give it a backing field and `SetField`.
- [x] 5.2 Same for `PasswordString`, `TotpSecret`, `TotpEnabled`, `AutoLockOnMinimize` and the root's `Name` — all persisted, none notifying.
- [x] 5.3 Confirm load does not now trigger a save: `InitializeRootNode` runs before `AddRootNode`, and `SaveConnectionsOnEdit` subscribes only once `ConnectionsLoaded` has been raised.
- [x] 5.4 `SaveConnectionsFrequency` ships as `Unassigned` and the migration off it runs only in `ConnectionsPage.LoadSettings`. At shutdown, fall back to the legacy `SaveConsOnExit` the migration reads.
- [x] 5.5 File → Save Connections called `SaveConnectionsAsync`. Write immediately, superseding any armed debounce so the same state is not written twice.
- [x] 5.6 Tests: each root property notifies; setting one to its current value does not.

## 6. Let a failed write be seen

- [x] 6.1 `XmlConnectionsSaver.Save` caught everything and logged, so `ConnectionsService` raised `ConnectionsSaved` and logged success over a file it had not written. Log for the stack trace, then rethrow.
- [x] 6.2 `FileDataProvider.Save` did the same underneath it, and detected a zero-byte result after replace without telling anyone. Route both through an overridable `HandleSaveException`, and make the zero-byte case a failure rather than a log line.
- [x] 6.3 Propagate only for `FileDataProviderWithRollingBackup` — the connection file. Settings, exports and credential writes keep the logging behaviour their callers were written against.

## Evidence

From the session that found it, in case the symptoms recur before this is fixed:

- The connection file's modification time did not change, and no rolling backup was created — every
  save rolls one, so its absence proves no write was attempted rather than a write going astray.
- The root's `Protected` attribute decodes to a 15-character plaintext (`ThisIsProtected`) while the
  backups from minutes earlier decode to 18 (`ThisIsNotProtected`), which is how the state was
  established without decrypting anything.
- Waiting a few seconds before quitting made the same sequence work.
