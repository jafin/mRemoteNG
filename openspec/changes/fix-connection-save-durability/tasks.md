# Tasks

Deferred. Written up while the evidence was fresh; not scheduled against the mRemoteNG#3416 audit
releases, which it is independent of.

## 1. Flush on exit

- [ ] 1.1 Give `ConnectionsService` a way to complete a pending debounced save synchronously: cancel the timer, and if a save was outstanding, perform it under `SaveLock`.
- [ ] 1.2 Call it from `Shutdown` **before** the conditional `SaveConnections`, and unconditionally — `SaveConnectionsFrequency` decides how often to save periodically, not whether the user's last edit survives.
- [ ] 1.3 Bound it. A write that cannot complete must not hang shutdown; time-box the flush and report rather than blocking.
- [ ] 1.4 Check the same gap on the other exit paths — close to tray, log off, and whatever `Shutdown.Cleanup` is reached by — rather than only the main one.
- [ ] 1.5 Tests: a pending save is written when shutdown runs; nothing is written when none is pending; the frequency setting does not suppress it.

## 2. Say when a save did not happen

- [ ] 2.1 Distinguish the four silent paths in `SaveConnections` — no model or file name, file not loaded, batching in progress, and the swallowed exception — and report the ones that mean the user's change was not stored.
- [ ] 2.2 Batching is not a failure: it defers to `_saveRequested`. Confirm that flag is always honoured afterwards, because if a batch ends without draining it the change is lost the same way and nothing says so.
- [ ] 2.3 Surface failures somewhere visible without opening the log, without a modal dialog per occurrence. Saves are frequent and mostly automatic.
- [ ] 2.4 Tests: a throwing save reports; a skipped save reports; a successful save reports nothing.

## 3. Keep the coalescing

- [ ] 3.1 Leave the two-second debounce alone. It exists because each save re-encrypts every password at 600,000 iterations, and removing it restores the problem it was added for.
- [ ] 3.2 Test that rapid successive changes still produce one write.

## 4. Verification

- [ ] 4.1 Full build; zero new analyzer warnings.
- [ ] 4.2 Full test suite; zero failures, no `[Ignore]`.
- [ ] 4.3 `openspec validate fix-connection-save-durability --strict`.
- [ ] 4.4 Manual, the case that found this: set a master password, remove it, save, quit immediately, restart. The password is gone.
- [ ] 4.5 Manual: rename a connection and quit within two seconds. The rename survives.
- [ ] 4.6 Manual: make the connection file read-only, edit something, quit. The failure is visible without opening the log, and shutdown still completes.

## Evidence

From the session that found it, in case the symptoms recur before this is fixed:

- The connection file's modification time did not change, and no rolling backup was created — every
  save rolls one, so its absence proves no write was attempted rather than a write going astray.
- The root's `Protected` attribute decodes to a 15-character plaintext (`ThisIsProtected`) while the
  backups from minutes earlier decode to 18 (`ThisIsNotProtected`), which is how the state was
  established without decrypting anything.
- Waiting a few seconds before quitting made the same sequence work.
