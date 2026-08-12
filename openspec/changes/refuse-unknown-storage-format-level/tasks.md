# Tasks

Ships **before** any change that adds a third level. The rule has to be in the build that precedes
the one introducing a level, or the first store written at that level meets an older build which
quietly flattens it. Landing it afterwards does not recover the files already downgraded.

## 1. Tell absent apart from unrecognised

- [x] 1.1 Give `StorageFormat` a resolution that reports three outcomes — absent, recognised, unrecognised — rather than folding the third into classic. Keep `Parse` or replace it, but no caller should be able to obtain a level for a value the build does not know. — `Parse` **replaced** by `Resolve`, returning `StorageFormatLevel?` with null for unrecognised, plus an `IsRecognised` predicate for the save side. Replaced rather than kept: leaving `Parse` in place would have left a method that still answers "classic" for a value the build does not know, and nothing stops the next caller reaching for it. Only one caller existed.
- [x] 1.2 Absence keeps its exact current meaning. This is the property under test, not a side effect: every file written before the level existed, and every file upstream mRemoteNG writes, must still open as classic. — `Resolve` returns classic for null, empty and whitespace, and `IsRecognised(null)` is true: absence is the classic *declaration*, not a missing one. Covered end to end by `TheTwoLevelsThatExistStillOpen`, which loads the real `confCons_v2_6` fixture unmodified.
- [x] 1.3 Tests: absent, empty and whitespace all resolve classic; `Hardened` in any case resolves hardened; an unknown value resolves to neither and is distinguishable from both. — `StorageFormatTests`, extended rather than replaced. The old `AnythingButHardenedReadsAsClassic` asserted the very behaviour this change removes, so its unknown-value case moved to `ADeclarationThisBuildDoesNotKnowResolvesToNoLevelAtAll`.

## 2. Refuse the read

- [x] 2.1 `XmlConnectionsDeserializer` refuses a store whose level is unrecognised, before any decryption is attempted and before any password is requested.
- [x] 2.2 Report it as a newer-build file. Not as a wrong password, and not as a corrupt file — both send the user looking for a problem that is not there. The SQL path's refusal of a newer database is the precedent to follow for wording and for where the message goes. — New `Language.ErrorConnectionFileFormatNewerThanClient`, worded on `ErrorDatabaseVersionNewerThanClient`: names the level, says no connections were loaded, says the file is unchanged, says to upgrade. **Written first as a `CTaskDialog`, then changed** to `Runtime.MessageCollector.AddMessage(ErrorMsg, …)` — the SQL precedent this task points at uses the collector, and a task dialog would also have made 2.3 an interactive test, which this repo forbids. The throw still reaches `Runtime.LoadConnections`, which already owns the dialog for a failed load.
- [x] 2.3 Tests: an unrecognised level throws rather than returning a tree; no authentication requestor is invoked. — `XmlConnectionsDeserializerStorageFormatTests`. The requestor is a delegate that fails the test if called, so "no password requested" is asserted by construction rather than by a flag checked afterwards. Confirmed non-vacuous by mutation: exempting `"Quantum"` from the guard fails both read-path tests and leaves the other four green.

## 3. Refuse the write

- [x] 3.1 A store that failed to open cannot be saved over. Confirm by inspection which save paths are reachable after a failed load — the rolling backup copies the file first, so a save that should not have happened destroys the original *and* fills a backup slot with the result. — **Inspection result: none are reachable.** Every save funnels through `ConnectionsService.SaveConnections`, which returns early on `!forceSave && !IsConnectionsFileLoaded`; the deserializer's `catch` sets that flag false, and no caller anywhere passes `forceSave: true` (the parameter exists but is never used). The no-argument overload bails separately on a null `ConnectionFileName`, which is only assigned after a load succeeds. The `Shutdown` autosave, the File menu's Save As and `StorageFormatCoordinator` all go through those same two gates.
- [x] 3.2 `XmlConnectionsSaver` does not write a classic file over a store whose level it could not resolve. — Added anyway, given 3.1 found the door already shut: `ThrowIfExistingFileLevelIsUnrecognised` reads the level off the file about to be overwritten and refuses. It is the second lock, justified by what one bypass costs — the rolling backup copies before writing, so a wrong save destroys the original and spends a backup slot. Reads only to the root element, so the cost does not scale with the file, and it matches the root element whatever it is called: this fork writes `mrng:Connections`, upstream writes a bare `Connections`, and matching by name would have silently skipped one shape and checked nothing. A file that is absent, unreadable or not XML is left alone — only a positively-read, positively-unrecognised level refuses a write, so an unrelated read fault cannot block saving.
- [x] 3.3 Tests: the file on disk is byte-identical after a refused load followed by whatever save paths 3.1 finds reachable. — `ARefusedStoreIsNotWrittenOver` asserts both halves of the damage: the bytes are unchanged *and* the directory still holds one file, so no backup slot was spent. `AStoreAtARecognisedLevelIsStillWritable` is its counterweight — the guard turning into "saving is refused" would be a worse defect than the one it prevents.

## 4. Verification

- [x] 4.1 Full build; zero new analyzer warnings. — Done 2026-08-12.
- [x] 4.2 Full test suite; zero failures, no `[Ignore]`. — Done 2026-08-12. 7,824 passed, 0 failed, 0 crashes (up 24 from the 7,800 before this change).
- [x] 4.3 `openspec validate refuse-unknown-storage-format-level --strict`. — Done 2026-08-12 — valid.
- [x] 4.4 Manual: hand-write `StorageFormat="Quantum"` onto a copy of a real connection file, open it, confirm the refusal names a newer build and that no password is asked for. — Done 2026-08-12, confirmed by the maintainer against a real connection file. The refusal is shown as an error dialog reading "The connection file declares storage format "Quantum", which this copy of mRemoteNG Connection Manager does not recognise, so it was written by a newer version. No connections were loaded and the file has not been changed." **No password prompt appeared** — which is the half of this task that could have failed, and the reason the check sits before `CreateDecryptor`. Incidentally confirms the resx/Designer wiring resolves at runtime and that a collector `ErrorMsg` does reach the user, via the popup writer.
- [x] 4.5 Manual: confirm the file is untouched afterwards — same bytes, no new backup. — Done 2026-08-12. File hash unchanged, no backup file created.
- [ ] 4.6 Manual: confirm a classic file and a hardened file both still open, so the check has not been applied to the two levels that exist.

4.6 is the only work left. It needs the application on a desktop and both a classic and a hardened
store to hand.

## Note on the reasoning this replaces

`StorageFormat.Parse` justifies collapsing unrecognised into classic on the grounds that such a file
is refused by the protection sentinel before anything is decrypted. That is true of the SQL store,
where `SqlConnectionsLoader` sets `PlaintextValidator = ConnectionFileDefaults.IsKnownSentinel`. It
is not true of the connection file, which builds its authenticator through `XmlConnectionsDecryptor`
and sets no validator at all — so `PasswordAuthenticator` accepts any plaintext that decrypts without
throwing.

The comment should be corrected when this lands, whichever way the change goes. It currently records
a guarantee the connection-file path does not provide.

**Done.** The corrected reasoning is now on `StorageFormat.Resolve`, stated as what was wrong rather
than quietly deleted: the sentinel argument holds for the SQL store, which sets
`PlaintextValidator = ConnectionFileDefaults.IsKnownSentinel`, and never held for the connection
file, which sets no validator — so `PasswordAuthenticator` accepts any plaintext that decrypts
without throwing, and nothing downstream would have caught the unknown level.
