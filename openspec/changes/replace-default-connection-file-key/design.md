# Design

## Context

`confCons.xml` is encrypted under one of two keys:

| Case | Key | Sentinel written |
|---|---|---|
| User set a master password | that password | `ThisIsProtected` |
| User set nothing | `mR3m` | `ThisIsNotProtected` |

`XmlRootNodeSerializer.cs:41` picks the sentinel by comparing `PasswordString` against
`DefaultPassword`. `RootNodeInfo.PasswordString` (`RootNodeInfo.cs:66`) returns the custom password
when one is set and `DefaultPassword` otherwise.

So the file already carries a one-of-N marker for how it is protected, read before anything is
decrypted. Adding a third value is the natural extension.

Two other mechanisms consume that file and constrain what the third value can mean:

| Mechanism | Where | Behaviour |
|---|---|---|
| Rolling backup | `FileBackupCreator.CreateBackupFile` | `File.Copy` of the encrypted file, N kept, location configurable |
| Automatic recovery | `XmlConnectionsLoader.TryRecoverFromBackup` | On load failure, tries every backup newest-first, overwrites the live file with the first that parses |

Both copy bytes. Neither re-encrypts. Whatever protects the live file protects every backup of it.

## Goals

- A connection file copied off the machine is not readable by whoever copied it.
- No existing file becomes unreadable.
- A user who has not set a master password is no worse off than today, and does not have to
  understand DPAPI to benefit.
- **Recoverability does not regress.** Today a lost machine costs nothing, because any backup opens
  anywhere. That property is worth keeping.

## Non-Goals

- Requiring a password at every start. The recovery password is set once and typed on recovery; it
  is not a master password in the existing sense and must not become one.
- Changing the cipher or the KDF. `harden-connection-file-kdf` owns those.
- Solving the SQL backend's use of `DefaultPassword`. `require-sql-master-password` owns it.
- Key escrow or enterprise recovery. Worth wanting; not this change.

## Decisions

### Two protectors for one file key

A random per-file key encrypts the contents. That key is stored twice in the file's root: once
wrapped by DPAPI, once wrapped by a key derived from a recovery password. Either unwraps it.

This is the standard multi-protector arrangement, and it is what makes every remaining decision
easy:

| Situation | Protector used |
|---|---|
| Daily use, same account | DPAPI, no prompt |
| Restored backup on a new machine | Recovery password |
| Rebuilt profile | Recovery password |
| Portable edition | Recovery password only — no DPAPI protector written |
| File stolen | Neither, unless the password is guessed |

An earlier draft had DPAPI alone plus a manual export. See "Why this changed" below.

### The recovery password is mandatory at migration

Not optional, not deferred. A file with one protector that cannot leave the machine is a file whose
entire backup history is worthless the moment the profile is rebuilt — and the user would have no
signal until they needed it.

This is close to the "force a master password" answer listed as a non-goal, and the difference is
real: it is typed at migration and at recovery, not at every launch. The daily experience is
unchanged, which is the property that makes it acceptable.

### DPAPI at CurrentUser, not LocalMachine

`LocalMachine` would survive a profile change and keep multi-user machines working, and it is the
wrong answer: any account on the box could decrypt the file, including a service account an attacker
already has. The threat is a file leaving the machine, and `CurrentUser` is what binds it. The
recovery password covers the cases `LocalMachine` would have covered, without the exposure.

### A third sentinel, not a new attribute

The sentinel is already read first and already discriminates protection. A parallel attribute would
give two sources of truth about the same question, and the first disagreement between them would be
a file that decrypts under one reading and not the other.

Concretely `ThisIsDpapiProtected`, with both wrapped keys in root attributes beside it. Any build
that does not know the value must refuse rather than guess — an unknown sentinel is not
`ThisIsNotProtected`.

### `mR3m` is read-only, not deleted

`ConnectionFileDefaults.LegacyEncryptionKey` stays, reachable only from the decrypt path. Deleting it
would strand every file written in the last fifteen years.

### Migration on save, not on load

Rewriting a file the user only opened is how a read-only inspection becomes a permanent change.
Migrating on the first save keeps the user's own action as the trigger, and the prompt fires before
the write.

### Backups stay byte-identical copies

`FileBackupCreator` copies the encrypted file and is not changed. It was worth asking whether backups
should carry only the password protector — omitting the DPAPI blob would make every backup portable
by construction and reveal slightly less about the machine that wrote it.

Decided against. It would mean backups are no longer copies: the backup path would have to unwrap the
file key, rewrap it, and write a different file, which puts key handling and a second write path into
the one mechanism whose whole value is that it is a `File.Copy` that cannot go wrong. The two-protector
model already makes a plain copy restorable anywhere, so the rewrite buys very little and costs the
simplicity of the thing users depend on when everything else has failed.

### Recovery must distinguish "cannot unwrap" from "cannot parse"

`TryRecoverFromBackup` exists for a corrupt file. A protector failure is not corruption: the backups
are fine and every one of them will fail identically. Iterating produces N misleading warnings and
buries the actual cause.

When the failure is an unwrap failure, recovery must stop and ask for the recovery password instead
of walking the backup set. The distinction is available — DPAPI unwrap failure surfaces as
`CryptographicException` and is separable from a deserialization error.

There is a second reason to be careful here: on success, recovery calls
`File.Copy(backupFile, _connectionFilePath, overwrite: true)` — it overwrites the live file before
returning. Reaching that path for the wrong reason destroys the current file.

## Portable edition

Portable writes the recovery-password protector and no DPAPI protector. Consequences worth stating:

- A portable file opens on any machine, which is the point of the edition.
- The installed edition can open a portable file — one of its two protectors is present.
- The portable edition can open an installed file, using the recovery password.
- A portable user who declines to set a password keeps `mR3m`, **and is told plainly** that the file
  is protected by a key published in the application's source. That is a smaller improvement than the
  installed edition gets, and it is honest, which the present silence is not.

The old draft had portable and installed producing mutually unreadable files. The two-protector
model removes that split; both editions write the same format and differ only in whether the DPAPI
protector is present.

## Why this changed

The first version of this design wrapped the file key with DPAPI only and offered a one-time export
as the escape route. Checking it against the backup feature showed the export does not carry the
weight:

- `FileBackupCreator` copies the encrypted bytes, so every rolling backup inherits the DPAPI binding.
- `BackupLocation` is configurable and users point it at shares and synced folders **specifically so
  backups survive the machine** — the exact case DPAPI breaks.
- The failure is silent. The copy succeeds and the file looks normal; only an attempted restore on
  another machine reveals that nothing in the backup set can be opened.

A change that improves confidentiality by reducing recoverability, without telling anyone, is not an
improvement. The second protector costs one prompt and removes the class.

## Risks

| Risk | Mitigation |
|---|---|
| Backups unreadable after machine loss | Recovery-password protector travels with every copy |
| Recovery password forgotten | DPAPI protector still works on the original machine; both are needed to lose the file |
| Recovery misreports a protector failure | Distinguish unwrap failure from parse failure; stop and prompt |
| Redirected-but-not-roaming profile | Recovery password covers it; detection is a nicety, not the safety net |
| Older build opens a migrated file | Unknown sentinel refuses with a message naming the version |
| Weak recovery password | Stretched with the KDF from `harden-connection-file-kdf` |

## Open Questions

- Should the installed edition offer to add a DPAPI protector to files that already have a master
  password, so the password stops being the only protector? It costs the user nothing and helps
  against a stolen file plus a guessed password. Leaning towards offering it separately rather than
  bundling it here.
- What happens on a shared workstation where two people use one Windows account? Nothing; DPAPI
  cannot help there. Worth stating in the docs rather than pretending otherwise.
