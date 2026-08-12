# Tasks

Depends on `add-storage-format-opt-in`, which owns the format level, the single confirmation and the
classic export — the per-file key is written only at the hardened level. Also depends on
`harden-connection-file-kdf`, which establishes that the file records its own cryptographic
parameters and provides the KDF that stretches the recovery password. Do not start before both land.

## 1. Export first

- [x] 1.1 Provide an export of the current connection file to a password-protected file, reachable before any migration prompt appears. — **Already delivered by `add-storage-format-opt-in` §1** and not rebuilt here. `Export.SaveExportFile` writes `SaveFormat.mRXML` under a caller-supplied password with `StorageFormatOverride = StorageFormatLevel.Classic`, so the export states its level rather than inheriting the store's. It is reachable from the File menu independently of any migration.
- [ ] 1.2 Tests: an exported file opens on another machine with its password; it carries no machine-bound protector. — The first half is covered by the existing export tests. The second half cannot be asserted until a store can actually be written with a machine-bound protector, which §5 is what enables — §3 built the protector but nothing selects it, so a test written today would pass because no file has one, not because the export drops it. Still deferred, now to §5, rather than written as a test that passes for the wrong reason.
- [x] 1.3 This is task 1 deliberately. Even with the recovery protector making migration survivable, a user must have a way out that does not depend on the new format being correct. — Holds: the way out predates this change and does not depend on any of it.

## 2. Protection declaration

- [x] 2.1 Add a third sentinel value for the per-file-key case alongside `ThisIsProtected` and `ThisIsNotProtected` in `XmlRootNodeSerializer.cs:41`. — `ConnectionFileDefaults.PerFileKeySentinel` = `ThisIsDpapiProtected`, and `IsKnownSentinel` now covers all three. Nothing writes it yet; §3 does.
- [x] 2.2 Make the reader refuse an unrecognised sentinel with a message naming the likely cause, rather than falling through to the legacy key. — The XML read path now supplies `IsKnownSentinel` as the `PlaintextValidator` that `require-sql-master-password` §1 added to `PasswordAuthenticator` but deliberately left the XML side without. **This closes a defect wider than the task describes:** the XML path previously accepted *any* plaintext whose decryption completed, and the legacy provider is AES-CBC with PKCS7 and no authentication tag, so a wrong key yields valid padding roughly once in 256 and returns arbitrary bytes rather than failing. Those arbitrary bytes were taken as proof of the password. The message itself is still to come — see the note below.
- [x] 2.3 Tests: each of the three values round-trips; an unknown value loads nothing and reports; the two existing values behave exactly as before. — `ConnectionFileSentinelTests`. The unknown-value case supplies an authentication requestor deliberately: without one the authenticator returns false before it ever decrypts, and the assertion would hold whether or not the validator existed. Confirmed non-vacuous by mutation — removing the validator fails that case alone.

**Regression caught while doing 2.2, worth recording.** The validator was first set inside
`XmlConnectionsDecryptor.Authenticate`, which is shared by two callers with different ciphertexts:
the sentinel check, and `LegacyFullFileDecrypt`, whose ciphertext is the *entire encrypted file*.
Validating the latter against the sentinel set refused every fully-encrypted legacy file with a
custom password — nine tests on `confCons v2.5 custompassword,fullencryption`. The validator is now
a parameter supplied only by the sentinel path, which is the one caller that knows what it
encrypted, and the reason is recorded on the parameter.

**Still open from 2.2:** the *message* naming the likely cause. A refusal today is silent — the load
simply fails. Wiring the message belongs with §3, where an unrecognised sentinel becomes reachable
in practice (a file written by a build that knows a fourth value) rather than hypothetical.

## 3. Two protectors

- [x] 3.1 Generate a random per-file key. Wrap it twice: with `ProtectedData.Protect` at `DataProtectionScope.CurrentUser`, and with a key derived from the recovery password using the KDF from `harden-connection-file-kdf`. Store both blobs in root attributes. — `Security/FileProtection/`: `ConnectionFileKey` (32 bytes from the system CSPRNG, zeroed on dispose), `DpapiKeyProtector`, `RecoveryPasswordKeyProtector`, and `ConnectionFileKeyProtection` holding the pair and writing them as `KeyProtectorMachine` and `KeyProtectorRecovery`. **The recovery blob carries its own KDF parameters** — version, PRF, iterations, salt — rather than reading the root's `KdfIterations`/`KdfPrf`: those describe how the *contents* are keyed, and at this level the contents are not keyed on a password at all. Two derivations sharing one pair of attributes is how a change to one silently breaks the other. The parameters are also authenticated as AES-GCM associated data.
- [x] 3.2 On load, try the machine protector first and fall back to prompting for the recovery password. Neither path may silently produce a wrong key. — `ConnectionFileKeyProtection.Unwrap`. The machine protector is tried silently; on failure the caller is handed the reason *before* any prompt, because a prompt on its own reads as "your password is wrong" when the cause is that the file came from another account. The password is re-asked up to three times, matching `PasswordAuthenticator`, and a cancelled prompt ends the attempt rather than counting as one. Neither path can produce a wrong key: DPAPI and AES-GCM both authenticate, so failure is an exception rather than plausible bytes — unlike the legacy AES-CBC path, where a wrong key yields valid padding roughly once in 256. **The single call site arrives with §5**, which is the change that first lets a store select this format; nothing writes the sentinel yet, so wiring the reader now would add a branch no file can reach.
- [x] 3.3 Confirm the unwrapped file key is used directly rather than fed to the KDF — it is already random and there is nothing to stretch. The KDF applies to the recovery password only. — Not merely confirmed: the existing providers all take a `SecureString` and derive, so using the key directly needed one that does not. `PerFileKeyCryptographyProvider` is AES-256-GCM under the file key with no derivation, and **its `SecureString` arguments are ignored deliberately** — every serializer call site passes `RootNodeInfo.PasswordString`, which at this level is not what the file is keyed on. Its wire format is distinct from the AEAD provider's for that reason: a caller that picked the wrong provider fails loudly instead of producing plausible bytes. `KeyDerivationIterations` and `KeyDerivationPrf` are inert, which the interface already allows for.
- [x] 3.4 Support adding or replacing the recovery protector on an already-migrated file, so a forgotten password is recoverable while the machine protector still works. — `WithRecoveryPassword`, plus `WithMachineProtector` for the other direction (a portable file opened by the installed edition, or a rebuilt profile). Only the protector changes; the file key is the same key, so nothing the file holds is re-encrypted and a rolling backup taken before the change still opens.
- [x] 3.5 Tests: unwrap by each protector independently; a file with only the password protector opens; changing the recovery password does not re-encrypt the contents; the two wrapped blobs are not equal and neither is a constant across files. — `ConnectionFileKeyProtectionTests` and `PerFileKeyCryptographyProviderTests`, 31 tests. Beyond the four asked for: the machine protector is asserted to prompt for *nothing* (by a requestor that fails the test if called — the daily path is what decides whether this design is acceptable at all); a file declaring a machine protector and no recovery protector is refused rather than accepted as one that opens today and can never be recovered; and the portable case writes no machine attribute at all, since an empty one would read back as a protector that fails and turn every portable open into a fallback with a message about another machine.

**Boundary worth stating.** §3 delivers the key and the two protectors; it does not yet make any
file use them. `RootNodeInfo` carries no protection object and neither serializer branches on one,
because the decision to write this format belongs to the single confirmation §5 owns. What that
leaves for §5 is the confirmation, one branch in `XmlRootNodeSerializer` to write the sentinel and
the two attributes, and one in `XmlConnectionsDeserializer` to read them and build
`PerFileKeyCryptographyProvider` instead of the password-keyed one.

## 4. Legacy key becomes read-only

- [ ] 4.1 Restrict `ConnectionFileDefaults.LegacyEncryptionKey` to the decrypt path; leave it reachable and documented as read-only.
- [ ] 4.2 Leave `SqlConnectionsLoader.cs:86,89` on the legacy default — a per-user key cannot serve a shared database, and `require-sql-master-password` owns that fix. Record why in the code, not only here.
- [ ] 4.3 Tests: no write path reaches the legacy key once a recovery password is set; existing files still decrypt.

## 5. Migration

- [ ] 5.1 Hook into the single confirmation from `add-storage-format-opt-in` rather than adding a prompt of your own. Contribute the recovery-password step and the explanation of why it is needed — another machine, a restored backup — to that one dialog.
- [ ] 5.2 Declining leaves the store at the classic level under the legacy key. The level is a property of the store, so this is remembered; the nag question belongs to `add-storage-format-opt-in` task 5.2, not here.
- [ ] 5.3 Detect a connection file living outside the user profile (redirected documents, a network share) and say so, since those users are the ones the machine protector will surprise. **The detection is load-bearing, not only informational — see 5.5.** It stays information as far as *migration* goes: the recovery protector is the safety net and nothing here blocks raising the level.
- [ ] 5.4 Tests: accepting migrates and writes both protectors; declining leaves the file byte-compatible with the old format; the prompt reappears on a later save.
- [ ] 5.5 Write no machine protector when 5.3 says the file is outside the user profile. Reuse `ConnectionFileKeyProtection.Create(includeMachineProtector: false)` — the same switch §7 uses for portable, not a second mechanism. The file carries one machine protector, so on a shared file it serves exactly one person and costs everyone else a prompt they cannot remove; see design.md, "Several people, one file". Being wrong here costs one prompt on a file that would not have prompted, and being wrong the other way costs every other member of a team a permanent one.
- [ ] 5.6 Keep a recovery password that has opened the store for the session, and drop it when the store locks. Without this, every path that re-reads the store — an external change, `TryRecoverFromBackup` — prompts again, which is how a password meant to be typed rarely becomes one typed constantly and therefore short. `XmlConnectionsDecryptor` already caches its decryption key for the same reason; the lock half is what stops this defeating `AutoLockOnMinimize`.
- [ ] 5.7 Tests: a file outside the profile is written with the recovery protector alone and reports no machine-protector failure when another account opens it; a file inside the profile still gets both; the session cache is not consulted after a lock.

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
- [ ] 8.9 Manual: point two Windows accounts at one connection file outside both profiles, migrate it from the first, and confirm the second opens it on the recovery password with no message about another account — and that the first is not prompted differently from the second. A second local account is enough; a share is not needed, only a path outside the profile.
