# Design

## Context

The fork and upstream mRemoteNG share storage:

| What | Where | Shared? |
|---|---|---|
| Connection file | `%APPDATA%\mRemoteNG\confCons.xml` | Yes — same directory, same name |
| Rolling backups | Same directory, `confCons.xml.*.backup` | Yes |
| SQL database | Whatever the team configured | Yes, and by more people |
| Application settings | Per-assembly user config | No |
| Log | `%LOCALAPPDATA%\mRemoteNG\mRemoteNG.log` | Shared path, but not a format contract |

`Application.ProductName` resolves such that the installed edition lands in `%APPDATA%\mRemoteNG`
regardless of the fork's `<Product>` string, so this is not something the fork drifted away from by
accident — it is deliberate compatibility, and it is worth keeping.

The connection file already carries the two things a level needs: a sentinel read before decryption
(`XmlRootNodeSerializer.cs:41`) and a precedent for self-describing parameters (`KdfIterations`).
The SQL store carries `ConfVersion`.

## Goals

- A user can evaluate this fork and go back to upstream with their data intact.
- A user who wants the hardening can have it, in one decision rather than four.
- Nothing changes a store's readability without being asked.

## Non-Goals

- Making hardened stores readable by upstream. That is a contradiction; the point of the format is
  that it does things upstream has no code for.
- Getting the hardening adopted by default. See "What this costs" in the proposal — that is given up
  on purpose.
- Diverging the fork's file location or name to sidestep the problem. Sharing the path is the
  compatibility, not the obstacle to it.
- Detecting whether upstream mRemoteNG is installed. See Decisions.
- Contributing the format upstream so both can read it. Worth doing and entirely separate; if it ever
  happened, `Hardened` would simply stop being fork-only and this change would have cost nothing.

## Decisions

### One level, one confirmation, not four migrations

Each hardening change could carry its own prompt. Four prompts appearing over a few weeks, each
describing a different cryptographic property, is how users learn to click through dialogs. Worse,
the state space multiplies: a store could be hardened-KDF but classic-key, and every combination
would need testing against upstream's behaviour.

A single level makes the question one a user can actually answer — *do I still need upstream
mRemoteNG to open this?* — and makes the compatibility claim testable, because there are two states
rather than sixteen.

### Classic is the default, permanently

Not "default for now, flipping in a later release". A user who declined last year must not find the
decision made for them by an upgrade. The level lives with the store, not with the application
version.

### The confirmation names the application, not the cryptography

"This store will use AES-256-GCM with PBKDF2-HMAC-SHA256" tells the user nothing about what they are
giving up. "mRemoteNG 1.82 and earlier, and the upstream mRemoteNG project, will no longer be able to
open this file" is the sentence that lets them decide. The cryptographic detail belongs below it, for
the people who want it.

### Classic export exists before the upgrade is offered

The escape hatch cannot be a later task. A user who upgrades and then wants out must not discover the
way back is unimplemented — that is precisely the lock-in this change exists to prevent, arriving one
release late.

Export produces a `Classic` file: legacy default key or master password, SHA-1 KDF, sentinel upstream
recognises. It is a lossy operation in the security sense and must say so.

### Backups follow the store

`FileBackupCreator` copies the live file, so a hardened store produces hardened backups. That is
correct and needs no special handling — but it means the upstream-readability of a user's backup set
changes at the moment they upgrade, which the confirmation must mention. Their existing classic
backups stay classic and stay readable; new ones do not.

### A classic store offers hardening once, then stops

A store at the classic level shows a dismissible note that hardening is available. It appears once
per store and is not shown again for that store after it is dismissed.

The tension is real in both directions: a security feature nobody discovers is a security feature
that shipped for nothing, and a repeated prompt about a decision the user already made deliberately
is how people learn to dismiss everything the application says — including the messages that matter.

Once per store settles it. It reaches users who would never look in options, and it treats a
dismissal as the answer to a question rather than as something to ask again next session. Per store
rather than per user, because someone with a personal file and a team SQL database has two different
decisions to make and the second one should still be offered.

Dismissal is remembered against the store, not the application install, so it survives a reinstall
and does not follow the user to a different file.

### No detection of whether upstream is installed

It was worth asking whether the fork should look for an upstream installation and refuse to offer the
upgrade while both are present. Decided against.

There is no reliable signal. Upstream ships portable builds that leave no registry entry and live
anywhere on disk, other forks share the same product identity, and a user may keep an installer
rather than an installation for exactly the rollback this change protects. Any check would be a
guess, and both wrong answers are bad: a false positive suppresses a security feature the user asked
for, and a false negative reads as an all-clear that was never true.

The confirmation already carries what detection would have been for. Telling the user plainly that
upstream will no longer open the store lets them decide from what they know about their own machine,
which is more than the application can find out.

### The SQL level is `ConfVersion`

`encrypt-sql-backend-with-aead` already gates on `ConfVersion` and already makes its upgrade an
explicit action. That *is* this design applied to the SQL store; this change renames the concept so
the two stores are described the same way, and requires the warning to name upstream rather than
"older clients".

The SQL case is the more serious of the two, because the person clicking the confirmation is not
the only person affected.

## What upstream actually does with a hardened file

Worth stating, because the confirmation wording depends on it and none of it is graceful:

| Change | Upstream's behaviour |
|---|---|
| Unknown `KdfPrf` | Attribute ignored; derives SHA-1; decrypt fails; reported as a wrong password |
| Unknown sentinel | Not recognised as either known value; the file does not open |
| SQL `ConfVersion` newer | Depends on the upstream verifier; at worst, legacy decrypt of AEAD ciphertext returning plausible bytes |

None of these say "this file was written by a newer application". A user who upgraded and went back
would see a password prompt they cannot satisfy, on a file they know the password to. That is why the
confirmation has to be explicit rather than relying on a graceful failure that does not exist.

## Risks

| Risk | Mitigation |
|---|---|
| Nobody finds the setting; hardening ships unused | State the store's level somewhere permanently visible, not only in options |
| User upgrades without understanding | Confirmation names the application, not the algorithm |
| User upgrades, wants out, export is missing | Export is task 1 |
| Colleagues locked out of a SQL store | Warning names upstream; the SQL upgrade is already explicit and password-gated |
| Level drifts per-artifact | One level per store, checked in one place |
