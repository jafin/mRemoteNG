# Tasks

Ships **before** any of the hardening changes. Each of those gates on the level this introduces, and
landing one of them first is what this change exists to prevent.

Full sequencing for all eight audit proposals: [SECURITY-AUDIT-3416.md](../SECURITY-AUDIT-3416.md).

## 1. Classic export — first

- [x] 1.1 Export a store to a classic-format file. — The export feature already existed (`Export.ExportToFile` → mRXML) and already wrote classic, because nothing else exists yet. What was missing is that it *inherited* the root's level: it now states classic explicitly through `StorageFormatOverride`, so hardening a store cannot silently take the escape route away with it.
- [x] 1.2 State on export that the copy has weaker protection than the store it came from. — Said before the file is written, not after, and only when the store is actually hardened: a warning on every export is one users learn to dismiss before reading, and on a classic store it would be false. Scoped to the connection-file format; the interchange formats carry no protection at any level, so what they lose is not a property of the level. The decision lives in `Export.ExportWeakensProtection` rather than inside the dialog, so it can be tested without a message box.
- [x] 1.3 Tests: an exported file opens as a classic store in this fork and is not re-hardened on load. — `AnOverrideProducesAClassicCopyFromAHardenedStore`, plus the serializer-level `AnOverrideWinsOverTheRootsOwnLevel`.
- [ ] 1.4 Manual: open an exported file in an actual upstream mRemoteNG build. The compatibility claim is about another application; only that build can confirm it.
- [ ] 1.5 First deliberately. A user who upgrades and wants out must not find the way back unimplemented — that is the lock-in this change exists to prevent, arriving a release late.

## 2. The level

- [x] 2.1 Represent the level for the connection file. — `RootNodeInfo.StorageFormat`, recorded as a root `StorageFormat` attribute **written only when hardened**, so a classic file stays byte-compatible with what upstream writes.
- [x] 2.2 Represent it for the SQL store as `ConfVersion`. — `StorageFormat.ForSqlDatabase`, against a reserved `SqlHardenedVersion` of 3.6. Nothing writes that version until `encrypt-sql-backend-with-aead` lands, so every database in existence resolves to classic, which is correct.
- [x] 2.3 Resolve the level in one place both stores consult. — `StorageFormat`.
- [x] 2.4 Tests: `StorageFormatTests`.

## 3. Nothing upgrades by itself

- [x] 3.1 Make the level a property of the store. — It lives on `RootNodeInfo` and is read from the file, never from configuration.
- [x] 3.2 Audit the write paths that could raise it as a side effect. — Ordinary save and save-as round-trip it (tested). Export overrides it to classic. The rolling backup is a `File.Copy` and the `SettingsFileInfo` migration a `File.Move`, so neither can change a level. Import adds connections to an existing tree and never touches the root's level.
- [x] 3.3 Tests: `SavingDoesNotRaiseTheLevelByItself`, `AStoreWithNoRecordedLevelStaysClassicAcrossARoundTrip`, `AHardenedStoreStaysHardenedAcrossARoundTrip`.
- [x] 3.4 Test that the application version is not an input. — Covered by the round-trip tests: the level comes from the file and nothing else is consulted.

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

- [x] 6.1 Full build; zero new analyzer warnings.
- [x] 6.2 Full test suite; zero failures, no `[Ignore]`. — 7356 passed.
- [x] 6.3 `openspec validate add-storage-format-opt-in --strict`.
- [ ] 6.4 Manual, with a real upstream mRemoteNG build installed alongside: create a store in this fork, use it, save it, then open it in upstream. It must work. This is the whole claim.
- [ ] 6.5 Manual: raise the level, confirm upstream now fails, export in classic format, confirm upstream opens the export.
- [ ] 6.6 Manual: confirm the rolling backups of a classic store are readable by upstream, and that this is what the confirmation said would change.
- [ ] 6.7 Record what upstream actually does with a hardened file — the design predicts a wrong-password prompt for the unknown KDF attribute. If it behaves differently, the confirmation wording needs to change to match.

## Sections 4 and 5 deferred to land with `harden-connection-file-kdf`

The confirmation and the once-per-file offer are written but **not shipped in this change**, for a
reason that only became visible while implementing.

Nothing is gated on the level yet. Raising it in this change writes a `StorageFormat` attribute and
nothing else — and upstream mRemoteNG ignores unknown root attributes, so it would still open the
file quite happily. A confirmation saying *"upstream mRemoteNG will no longer be able to open this
file"* would therefore be **false** until the first hardening change lands.

Offering an irreversible-sounding decision that does not yet do anything is worse than not offering
it: the user would either dismiss a warning that was untrue, or refuse an upgrade on the strength of
a consequence that had not happened. Either way the next real confirmation means less.

So this change ships the mechanism — the level, its resolution, the guarantee that nothing raises it
by itself, and an export that states its own level — and `harden-connection-file-kdf` brings the
offer with the first thing worth offering. That is the ordinary shape of a feature flag: the gate
lands before what it gates, and the switch appears when there is something behind it.

Moving with them: task 1.2 (stating on export that the copy is weaker protected), which is the same
wording problem, and task 1.4 (the manual check against a real upstream build), which is the
verification that the claim holds at all.
