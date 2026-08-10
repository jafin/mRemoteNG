# Tasks

Depends on `add-storage-format-opt-in`, which owns the format level, the single confirmation and the
classic export — the per-file key is written only at the hardened level. Also depends on
`harden-connection-file-kdf`, which establishes that the file records its own cryptographic
parameters and provides the KDF that stretches the recovery password. Do not start before both land.

## 1. Export first

- [ ] 1.1 Provide an export of the current connection file to a password-protected file, reachable before any migration prompt appears.
- [ ] 1.2 Tests: an exported file opens on another machine with its password; it carries no machine-bound protector.
- [ ] 1.3 This is task 1 deliberately. Even with the recovery protector making migration survivable, a user must have a way out that does not depend on the new format being correct.

## 2. Protection declaration

- [ ] 2.1 Add a third sentinel value for the per-file-key case alongside `ThisIsProtected` and `ThisIsNotProtected` in `XmlRootNodeSerializer.cs:41`.
- [ ] 2.2 Make the reader refuse an unrecognised sentinel with a message naming the likely cause, rather than falling through to the legacy key.
- [ ] 2.3 Tests: each of the three values round-trips; an unknown value loads nothing and reports; the two existing values behave exactly as before.

## 3. Two protectors

- [ ] 3.1 Generate a random per-file key. Wrap it twice: with `ProtectedData.Protect` at `DataProtectionScope.CurrentUser`, and with a key derived from the recovery password using the KDF from `harden-connection-file-kdf`. Store both blobs in root attributes.
- [ ] 3.2 On load, try the machine protector first and fall back to prompting for the recovery password. Neither path may silently produce a wrong key.
- [ ] 3.3 Confirm the unwrapped file key is used directly rather than fed to the KDF — it is already random and there is nothing to stretch. The KDF applies to the recovery password only.
- [ ] 3.4 Support adding or replacing the recovery protector on an already-migrated file, so a forgotten password is recoverable while the machine protector still works.
- [ ] 3.5 Tests: unwrap by each protector independently; a file with only the password protector opens; changing the recovery password does not re-encrypt the contents; the two wrapped blobs are not equal and neither is a constant across files.

## 4. Legacy key becomes read-only

- [ ] 4.1 Restrict `ConnectionFileDefaults.LegacyEncryptionKey` to the decrypt path; leave it reachable and documented as read-only.
- [ ] 4.2 Leave `SqlConnectionsLoader.cs:86,89` on the legacy default — a per-user key cannot serve a shared database, and `require-sql-master-password` owns that fix. Record why in the code, not only here.
- [ ] 4.3 Tests: no write path reaches the legacy key once a recovery password is set; existing files still decrypt.

## 5. Migration

- [ ] 5.1 Hook into the single confirmation from `add-storage-format-opt-in` rather than adding a prompt of your own. Contribute the recovery-password step and the explanation of why it is needed — another machine, a restored backup — to that one dialog.
- [ ] 5.2 Declining leaves the store at the classic level under the legacy key. The level is a property of the store, so this is remembered; the nag question belongs to `add-storage-format-opt-in` task 5.2, not here.
- [ ] 5.3 Detect a connection file living outside the user profile (redirected documents, a network share) and say so, since those users are the ones the machine protector will surprise. This is information, not a gate — the recovery protector is the safety net.
- [ ] 5.4 Tests: accepting migrates and writes both protectors; declining leaves the file byte-compatible with the old format; the prompt reappears on a later save.

## 6. Backups and recovery

- [ ] 6.1 Leave `FileBackupCreator.CreateBackupFile` as a `File.Copy` — decided, see design.md. A copy carrying both protectors is already restorable anywhere, and rewrapping per backup would put key handling into the one mechanism whose value is that it cannot go wrong. Add a test asserting a copy restores, rather than assuming it.
- [ ] 6.2 In `XmlConnectionsLoader.TryRecoverFromBackup`, separate an unwrap failure from a parse failure. On unwrap failure, do not walk the backup set: prompt for the recovery password instead.
- [ ] 6.3 Confirm the `File.Copy(backupFile, _connectionFilePath, overwrite: true)` at the end of a successful recovery cannot be reached on a protector failure. Recovery overwrites the live file, so reaching it for the wrong reason is destructive.
- [ ] 6.4 Tests: a backup restored on another machine opens with the recovery password; a backup restored on the original machine opens with no prompt; a protector failure produces one clear message rather than one warning per backup; the live file is untouched on that path.
- [ ] 6.5 Test the `BackupLocation` case specifically — backups written to a different directory, then opened from a machine that did not write them. This is the workflow the single-protector design would have broken and the reason the design changed.

## 7. Portable edition

- [ ] 7.1 Gate the machine-bound protector on `Runtime.IsPortableEdition` so portable writes only the recovery-password protector.
- [ ] 7.2 On decline, state plainly that the file is protected by a key published in the application's source.
- [ ] 7.3 Tests: portable never writes a machine-bound protector; a portable file opens on a second machine.
- [ ] 7.4 Tests: an installed file opens in the portable edition with its recovery password, and a portable file opens in the installed edition. The two editions must not produce a format split.

## 8. Verification

- [ ] 8.1 Full build; zero new analyzer warnings.
- [ ] 8.2 Full test suite; zero failures, no `[Ignore]`.
- [ ] 8.3 `openspec validate replace-default-connection-file-key --strict`.
- [ ] 8.4 Manual: migrate a real file, confirm connections still open with no prompt, confirm the file no longer decrypts with `mR3m` using an independent script.
- [ ] 8.5 Manual: copy a migrated file to a second Windows account, confirm the recovery password opens it and the message before that names the cause.
- [ ] 8.6 Manual: let the rolling backup run, copy the backup directory to another machine, restore from it with the recovery password. **This is the scenario that changed the design; verify it by hand, not only in tests.**
- [ ] 8.7 Manual: portable edition on two machines from one USB stick, with and without a recovery password, and a file exchanged between portable and installed.
- [ ] 8.8 Manual: open a migrated file with the previous release and confirm it refuses with a version message rather than corrupting anything.
