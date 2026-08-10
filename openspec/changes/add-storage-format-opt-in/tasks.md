# Tasks

Ships **before** any of the hardening changes. Each of those gates on the level this introduces, and
landing one of them first is what this change exists to prevent.

Full sequencing for all eight audit proposals: [SECURITY-AUDIT-3416.md](../SECURITY-AUDIT-3416.md).

## 1. Classic export — first

- [ ] 1.1 Export a store to a classic-format file: legacy default key or master password, SHA-1 KDF, a sentinel upstream recognises.
- [ ] 1.2 State on export that the copy has weaker protection than the store it came from.
- [ ] 1.3 Tests: an exported file opens as a classic store in this fork and is not re-hardened on load.
- [ ] 1.4 Manual: open an exported file in an actual upstream mRemoteNG build. The compatibility claim is about another application; only that build can confirm it.
- [ ] 1.5 First deliberately. A user who upgrades and wants out must not find the way back unimplemented — that is the lock-in this change exists to prevent, arriving a release late.

## 2. The level

- [ ] 2.1 Represent the level for the connection file. Absence means classic, per `harden-connection-file-kdf`'s rule that an unmarked file keeps its historical interpretation.
- [ ] 2.2 Represent it for the SQL store as `ConfVersion`, which `encrypt-sql-backend-with-aead` already gates on. One concept, two encodings; do not invent a second SQL marker.
- [ ] 2.3 Resolve the level in one place both stores consult, so a hardening change cannot bypass it by reading a setting directly.
- [ ] 2.4 Tests: an unmarked file resolves classic; a marked one resolves hardened; an unreadable marker resolves classic rather than throwing.

## 3. Nothing upgrades by itself

- [ ] 3.1 Make the level a property of the store, never of the application version or a global setting.
- [ ] 3.2 Audit the write paths that could raise it as a side effect: ordinary save, save-as, import, the automatic backup, and the connection-file migration in `SettingsFileInfo`.
- [ ] 3.3 Tests: opening, editing and saving a classic store leaves it classic; a save-as from a classic store produces a classic file; an import into a classic store does not raise it.
- [ ] 3.4 Test that the application version is not an input. A build newer than the one that created the store must leave it alone.

## 4. Confirmation

- [ ] 4.1 One confirmation covering all hardening, not one per change.
- [ ] 4.2 Word it around applications rather than algorithms: upstream mRemoteNG and earlier builds of this fork will no longer open the store. Cryptographic detail goes below that, for those who want it.
- [ ] 4.3 State that existing backups stay readable and new ones will not.
- [ ] 4.4 Offer the classic export from task 1 in the confirmation itself.
- [ ] 4.5 For a SQL store, add that every client must be upgraded — the person confirming is not the only one affected.
- [ ] 4.6 Tests: confirming raises the level; declining leaves the store byte-compatible with what upstream reads.

## 5. Visibility

- [ ] 5.1 Show the current store's level without requiring the options dialog.
- [ ] 5.2 Offer the upgrade once per classic **connection file**, dismissible. Record the dismissal against the file rather than the install, so it survives a reinstall and does not follow the user to a different file.
- [ ] 5.3 Do **not** offer it for a SQL store. That upgrade lives in the SQL options page — see `encrypt-sql-backend-with-aead` task 4.1. The decision is team-wide and belongs to whoever administers the database, not to whoever opens the application first; an accepting click from a user without that authority costs their colleagues access.
- [ ] 5.4 Keep the upgrade reachable on request after dismissal. Dismissing is an answer, not an opt-out.
- [ ] 5.5 Tests: the level is reported for both classic and hardened stores; the offer appears once, not after dismissal, and independently per connection file; a classic SQL store produces no offer.

## 6. Verification

- [ ] 6.1 Full build; zero new analyzer warnings.
- [ ] 6.2 Full test suite; zero failures, no `[Ignore]`.
- [ ] 6.3 `openspec validate add-storage-format-opt-in --strict`.
- [ ] 6.4 Manual, with a real upstream mRemoteNG build installed alongside: create a store in this fork, use it, save it, then open it in upstream. It must work. This is the whole claim.
- [ ] 6.5 Manual: raise the level, confirm upstream now fails, export in classic format, confirm upstream opens the export.
- [ ] 6.6 Manual: confirm the rolling backups of a classic store are readable by upstream, and that this is what the confirmation said would change.
- [ ] 6.7 Record what upstream actually does with a hardened file — the design predicts a wrong-password prompt for the unknown KDF attribute. If it behaves differently, the confirmation wording needs to change to match.
