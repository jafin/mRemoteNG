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

## Several people, one file

A team sharing one `confCons.xml` — on a network share, in a synced folder — is not the case this
design was drawn for, and it does not fall out of it cleanly. Recorded here because the answer is a
deliberate restriction rather than something a reader would predict from the rest.

The file holds **one** machine protector. Whoever migrates it writes their own DPAPI blob; every
other member fails that blob and falls back to the recovery password on every open, permanently.
There is no second slot, so nothing they do makes the prompt stop. Their saves write the first
person's blob back unchanged, so the arrangement is at least stable — it is simply wrong for
everyone but one member of the team.

That makes the recovery password a de-facto shared master password. For a team that already sets
one, nothing changes. For a team sharing an unprotected file — the group this change exists to help,
since their file is readable today by anyone who copies it off the share — it is a new prompt on
every open, for everyone.

### A shared file gets no machine protector

A protector only one member can use is worse than no protector at all: it costs every other member a
prompt, and the message before that prompt tells them the file was protected by a different account,
which is true and useless. So a connection file that does not live under the user's profile is
written with the recovery protector alone.

This is the same shape as the portable edition and reuses the same switch. Task 5.3 already detects
the location in order to *inform* the user; this makes the detection load-bearing.

The detection cannot be exact — a redirected Documents folder is a share the user does not know they
have, and a personal file on a NAS is not a team file. It does not need to be exact. Being wrong in
the "no machine protector" direction costs one prompt per open on a file that would otherwise not
have prompted; being wrong the other way costs every other member of a team a prompt they can never
get rid of. The asymmetry decides it.

### What a shared file does and does not get

Worth stating plainly, because the summary "your connection file is now encrypted under a key that
is not published in our source" is true for a shared file and means less than it sounds.

| | Single user | Shared file |
|---|---|---|
| Readable by whoever copies it off the disk | No | Not without the shared secret |
| Readable by a member who has left the team | No | **Yes**, until the file is rekeyed |
| Prompt on daily use | None | One, for everyone |

The middle row is the honest limit. A shared secret cannot be taken away from someone who has
already learnt it, so removing a member means choosing a new recovery password *and* a new file key,
and re-encrypting the contents under it. That is a real operation this change does not provide.

It is still strictly better than what it replaces, where the secret is `mR3m` and every member of
every team already has it, along with everybody else.

### Key slots are the answer, and are not here

The arrangement that actually fits a team is the one BitLocker and LUKS use: several protectors, any
of which opens the same key. Each member's first open uses the recovery password, their own DPAPI
blob is added as a slot, and every open after that is silent — for all of them, not one of them.

It is deliberately not in this change. It turns a single root attribute into a set, which is a format
change on top of a format change, and it needs a revocation story — removing a slot is only
meaningful together with the rekey described above, or the departed member's copy of the file still
opens. Both belong in `add-connection-file-key-slots`, sequenced after this.

Until then a team is in the position described at the top of this section, which is a working
position and a documented one.

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
- **Answered:** what happens to a team sharing one file. See "Several people, one file" above. The
  short version is that they get the recovery password as a shared secret and no machine protector,
  and that key slots — the arrangement that would actually serve them — are deferred to their own
  change.
