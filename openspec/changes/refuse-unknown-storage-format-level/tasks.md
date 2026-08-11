# Tasks

Ships **before** any change that adds a third level. The rule has to be in the build that precedes
the one introducing a level, or the first store written at that level meets an older build which
quietly flattens it. Landing it afterwards does not recover the files already downgraded.

## 1. Tell absent apart from unrecognised

- [ ] 1.1 Give `StorageFormat` a resolution that reports three outcomes — absent, recognised, unrecognised — rather than folding the third into classic. Keep `Parse` or replace it, but no caller should be able to obtain a level for a value the build does not know.
- [ ] 1.2 Absence keeps its exact current meaning. This is the property under test, not a side effect: every file written before the level existed, and every file upstream mRemoteNG writes, must still open as classic.
- [ ] 1.3 Tests: absent, empty and whitespace all resolve classic; `Hardened` in any case resolves hardened; an unknown value resolves to neither and is distinguishable from both.

## 2. Refuse the read

- [ ] 2.1 `XmlConnectionsDeserializer` refuses a store whose level is unrecognised, before any decryption is attempted and before any password is requested.
- [ ] 2.2 Report it as a newer-build file. Not as a wrong password, and not as a corrupt file — both send the user looking for a problem that is not there. The SQL path's refusal of a newer database is the precedent to follow for wording and for where the message goes.
- [ ] 2.3 Tests: an unrecognised level throws rather than returning a tree; no authentication requestor is invoked.

## 3. Refuse the write

- [ ] 3.1 A store that failed to open cannot be saved over. Confirm by inspection which save paths are reachable after a failed load — the rolling backup copies the file first, so a save that should not have happened destroys the original *and* fills a backup slot with the result.
- [ ] 3.2 `XmlConnectionsSaver` does not write a classic file over a store whose level it could not resolve.
- [ ] 3.3 Tests: the file on disk is byte-identical after a refused load followed by whatever save paths 3.1 finds reachable.

## 4. Verification

- [ ] 4.1 Full build; zero new analyzer warnings.
- [ ] 4.2 Full test suite; zero failures, no `[Ignore]`.
- [ ] 4.3 `openspec validate refuse-unknown-storage-format-level --strict`.
- [ ] 4.4 Manual: hand-write `StorageFormat="Quantum"` onto a copy of a real connection file, open it, confirm the refusal names a newer build and that no password is asked for.
- [ ] 4.5 Manual: confirm the file is untouched afterwards — same bytes, no new backup.
- [ ] 4.6 Manual: confirm a classic file and a hardened file both still open, so the check has not been applied to the two levels that exist.

## Note on the reasoning this replaces

`StorageFormat.Parse` justifies collapsing unrecognised into classic on the grounds that such a file
is refused by the protection sentinel before anything is decrypted. That is true of the SQL store,
where `SqlConnectionsLoader` sets `PlaintextValidator = ConnectionFileDefaults.IsKnownSentinel`. It
is not true of the connection file, which builds its authenticator through `XmlConnectionsDecryptor`
and sets no validator at all — so `PasswordAuthenticator` accepts any plaintext that decrypts without
throwing.

The comment should be corrected when this lands, whichever way the change goes. It currently records
a guarantee the connection-file path does not provide.
