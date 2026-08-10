# Tasks

Ships **before** any of the hardening changes. Each of those gates on the level this introduces, and
landing one of them first is what this change exists to prevent.

Full sequencing for all eight audit proposals: [SECURITY-AUDIT-3416.md](../SECURITY-AUDIT-3416.md).

## 1. Classic export — first

- [x] 1.1 Export a store to a classic-format file. — The export feature already existed (`Export.ExportToFile` → mRXML) and already wrote classic, because nothing else exists yet. What was missing is that it *inherited* the root's level: it now states classic explicitly through `StorageFormatOverride`, so hardening a store cannot silently take the escape route away with it.
- [x] 1.2 State on export that the copy has weaker protection than the store it came from. — Said before the file is written, not after, and only when the store is actually hardened: a warning on every export is one users learn to dismiss before reading, and on a classic store it would be false. Scoped to the connection-file format; the interchange formats carry no protection at any level, so what they lose is not a property of the level. The decision lives in `Export.ExportWeakensProtection` rather than inside the dialog, so it can be tested without a message box.
- [x] 1.3 Tests: an exported file opens as a classic store in this fork and is not re-hardened on load. — `AnOverrideProducesAClassicCopyFromAHardenedStore`, plus the serializer-level `AnOverrideWinsOverTheRootsOwnLevel`.
- [x] 1.4 Manual: open an exported file in an actual upstream mRemoteNG build. The compatibility claim is about another application; only that build can confirm it. — Passed, exported from a hardened store, both with and without full-file encryption. The file was also read directly: the root element carries neither `StorageFormat` nor `KdfPrf`, so the override is doing the work rather than the store happening to be classic. Upstream `v1.78.2-dev` reads `KdfIterations` (`XmlConnectionsDeserializer.cs:131`), so the 600,000 count this fork raised to in v1.80.0 is honoured and not silently replaced with upstream's own — the escape route survives the iteration count as well as the level.
- [x] 1.5 First deliberately. A user who upgrades and wants out must not find the way back unimplemented — that is the lock-in this change exists to prevent, arriving a release late. — Held. The export landed and was verified against a real upstream build (1.4) before the confirmation existed to offer it, and before anything could raise a level. The ordering is visible in the commit sequence rather than only asserted here: the export, then the warning on it, then the confirmation, then the offer.

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

- [x] 4.1 One confirmation covering all hardening, not one per change. — `StorageFormatUpgrade`, which every hardening change reaches through the level rather than by adding a prompt of its own.
- [x] 4.2 Word it around applications rather than algorithms: upstream mRemoteNG and earlier builds of this fork will no longer open the store. Cryptographic detail goes below that, for those who want it. — The detail sits behind the task dialog's expander, which is what "below" means in practice: reachable, not competing. Tested as a separation rather than as wording — `TheCryptographyIsBelowThePartThatMatters` asserts PBKDF2 appears in the expanded text and **not** in the main content, so the two cannot be merged by a later edit without a test failing.
- [x] 4.3 State that existing backups stay readable and new ones will not.
- [x] 4.4 Offer the classic export from task 1 in the confirmation itself. — A named command button, not a Yes/No/Cancel position the user has to interpret. **Choosing it returns to the question rather than answering it**: the export is something to do *before* deciding, so treating it as an answer would leave a user who wanted both with only the copy and no signal that the store was never hardened.
- [x] 4.5 For a SQL store, add that every client must be upgraded — the person confirming is not the only one affected.
- [x] 4.6 Tests: confirming raises the level; declining leaves the store byte-compatible with what upstream reads. — `StorageFormatUpgradeTests`, eight cases. The byte-compatibility one serializes after declining and asserts neither `StorageFormat` nor `KdfPrf` reaches the file, because the claim is about the file rather than about a property. Taking the classic copy is covered alongside declining: it must not raise the level either.

**Rendering confirmed manually.** Three buttons each with their second line, body text and expanded detail inside the frame. Getting there took three fixes to the emulated task dialog, all pre-existing and all affecting every dialog in the application that sets content: `lbContent` and `lbExpandedInfo` were anchored `Top|Right|Right` with `Left` mistyped as `Right`, so their text slid off the left edge on any resize; label heights were measured with GDI+ `MeasureString` while a `Label` draws through GDI, which measures wider, so the last line or two fell outside the panel; and command buttons were sized from `Width` rather than `ClientSize.Width`, so each was built wider than the area it draws into. None of this was reachable by a test — the dialog had to be looked at.

## 5. Visibility

- [x] 5.1 Show the current store's level without requiring the options dialog. — `RootNodeInfo.StorageFormatDisplay`, read-only, in the property grid beside the store's other security settings. On the root node because the level is a property of *this store* and the options dialog is a property of the application; a user who has to go looking in options to find out whether their file is readable by anything else will not go looking. Read-only because raising it is a decision with a confirmation attached, not a dropdown.
- [x] 5.2 Offer the upgrade once per classic **connection file**, dismissible. Record the dismissal against the file rather than the install, so it survives a reinstall and does not follow the user to a different file. — A hidden sidecar beside the store (`<store>.hardening-declined`). **Not inside the connection file**, which would be the exact unknown construct the classic-compatibility rule exists to keep out, and not in application settings, which would not survive a reinstall and would not travel with a store copied to another machine. Offered from `ConnectionsLoaded`, posted rather than called so a modal dialog does not hold up the rest of the load.
- [x] 5.3 Do **not** offer it for a SQL store. That upgrade lives in the SQL options page — see `encrypt-sql-backend-with-aead` task 4.1. The decision is team-wide and belongs to whoever administers the database, not to whoever opens the application first; an accepting click from a user without that authority costs their colleagues access. — Refused in `StorageFormatOffer.ShouldOffer` rather than at the call site, so a later caller cannot reintroduce it by forgetting.
- [x] 5.4 Keep the upgrade reachable on request after dismissal. Dismissing is an answer, not an opt-out. — File ▸ Storage Format..., which ignores the recorded decline entirely. An already-hardened store gets told so, and pointed at the export, rather than being offered an upgrade it has had.
- [x] 5.5 Tests: the level is reported for both classic and hardened stores; the offer appears once, not after dismissal, and independently per connection file; a classic SQL store produces no offer. — `StorageFormatOfferTests`, nine cases against a real temporary directory rather than a mocked file system, since the claim being made is about files on disk. Includes `TheRecordIsNotWrittenIntoTheStore`, which asserts the store's bytes are untouched by recording a decline.

**An unwritable record fails toward asking again.** If the sidecar cannot be written — a read-only directory, a store on removable media — the decline is reported to the message collector and the offer returns next session. The alternative, treating an unrecordable answer as permanent, silently suppresses a security feature the user would never be told about again.

## 6. Verification

- [x] 6.1 Full build; zero new analyzer warnings.
- [x] 6.2 Full test suite; zero failures, no `[Ignore]`. — 7356 passed.
- [x] 6.3 `openspec validate add-storage-format-opt-in --strict`.
- [x] 6.4 Manual, with a real upstream mRemoteNG build installed alongside: create a store in this fork, use it, save it, then open it in upstream. It must work. This is the whole claim. — Passed against upstream v1.78.2-dev.
- [x] 6.5 Manual: raise the level, confirm upstream now fails, export in classic format, confirm upstream opens the export. — Passed, and the first run of the whole path through the user interface rather than by hand: File ▸ Storage Format ▸ Harden, upstream then refuses the store, export, upstream opens the export.
- [x] 6.6 Manual: confirm the rolling backups of a classic store are readable by upstream, and that this is what the confirmation said would change. — Both halves. A rolling backup of a classic store opens in upstream; a backup taken after hardening does not, and fails the same way the store itself does. That is the confirmation's sentence about backups demonstrated in both directions rather than only the alarming one.
- [x] 6.7 Record what upstream actually does with a hardened file — the design predicts a wrong-password prompt for the unknown KDF attribute. If it behaves differently, the confirmation wording needs to change to match. — **The prediction holds.** Upstream prompts for the password and refuses the correct one, with nothing said about the format. No wording change needed: the confirmation already says "They will not tell you why. They ask for the password again, and refuse the one you give them, on a file you know the password to."

## Sections 4 and 5 deferred to land with `harden-connection-file-kdf` — done

**Landed, on the `security/harden-connection-file-kdf` branch, as planned below.** The condition the
deferral was waiting on is met: that change is complete, so a hardened store now genuinely does not
open in upstream mRemoteNG and the confirmation's central sentence is true when it is shown. Tasks
1.2 and 1.4 moved with them and are done.

The reasoning is kept because it is the argument for the ordering, not a note about work outstanding.

---

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
