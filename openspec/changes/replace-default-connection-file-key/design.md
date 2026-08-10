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
decrypted. Adding a third value is the natural extension, and it is why this change is cheap to
express and expensive only in what it implies for the user.

## Goals

- A connection file copied off the machine is not readable by whoever copied it.
- No existing file becomes unreadable.
- A user who has not set a master password is no worse off than today, and does not have to
  understand DPAPI to benefit.

## Non-Goals

- Forcing a master password. Upstream's audit offers this as the alternative; it converts a silent
  weakness into a support burden and an adoption cliff, and users who are made to invent a password
  they did not want will pick a bad one or write it down.
- Changing the cipher or the KDF. `harden-connection-file-kdf` owns those. This changes only what the
  key is and where it comes from.
- Solving the SQL backend's use of `DefaultPassword`. See below.
- Key escrow, recovery codes, or a second protector. Worth wanting; not this change.

## Decisions

### DPAPI at CurrentUser, not LocalMachine

`LocalMachine` scope would survive a profile change and keep multi-user machines working, and it is
the wrong answer: any account on the box could decrypt the file, including a service account an
attacker already has. The threat is a file leaving the machine, and `CurrentUser` is what binds it.

### A third sentinel, not a new attribute

The sentinel is already read first and already discriminates protection. Introducing a parallel
attribute would give two sources of truth about the same question, and the first disagreement
between them would be a file that decrypts under one reading and not the other.

Concretely: `ThisIsDpapiProtected`, with the wrapped key in a root attribute beside it. Any build
that does not know the value must refuse rather than guess — an unknown sentinel is not
`ThisIsNotProtected`.

### `mR3m` is read-only, not deleted

`ConnectionFileDefaults.LegacyEncryptionKey` stays, reachable only from the decrypt path. Deleting it
would strand every file written in the last fifteen years. Keeping it while never writing under it is
what makes the migration one-way and safe.

### Migration on save, not on load

Rewriting a file the user only opened is how a read-only inspection becomes a permanent change to
something they cannot undo. Migrating on the first save keeps the user's own action as the trigger,
and the notice fires before the write rather than after.

### Export before migrate

The migration notice offers an export to a password-protected file. This is not a nicety: it is the
answer to "I reinstalled Windows and my connections are gone", and it has to exist before the
migration is offered, not after the first person hits it.

### Portable keeps the legacy key by default

`Runtime.IsPortableEdition` already distinguishes the build. A DPAPI-wrapped key on a USB stick is
unreadable on the next machine, which is the entire use case. Portable is offered a master password;
if declined, it stays on `mR3m` **and says so** — a visible, dismissible statement that the file is
effectively unencrypted, rather than the current silence.

That is a smaller improvement than the installed edition gets, and it is honest, which the present
state is not.

## The SQL backend

`SqlConnectionsLoader.cs:86,89` uses `DefaultPassword` as the fallback when a database has no master
password. A per-user DPAPI key cannot serve a store multiple clients read, so the SQL path keeps the
legacy default and is unchanged by this proposal.

It is a real hole and it is a different one: the answer for a shared store is a required master
password or a key held elsewhere, not a per-user protector. `encrypt-sql-backend-with-aead` fixes the
cipher and KDF for that backend but explicitly does not touch this. Worth its own proposal.

## Risks

| Risk | Mitigation |
|---|---|
| Profile rebuild loses the file | Export offered before migration; notice states the binding |
| Redirected-but-not-roaming profile | Detect where the file lives relative to the profile and warn |
| Portable user gets a machine-bound file | Portable never migrates to DPAPI |
| Older build opens a migrated file | Unknown sentinel refuses with a message naming the version |
| User migrates without understanding | Notice states the consequence and requires confirmation |

## Open Questions

- Should an installed edition that already has a master password be offered DPAPI as well, so the
  password is not the only protector? It would help against a stolen file plus a guessed password,
  and it costs the user nothing. It also makes the file machine-bound for people who deliberately
  chose portability via password. Leaning towards offering it as a separate opt-in rather than
  bundling it here.
- What happens on a shared workstation where two people use one Windows account? Nothing; DPAPI
  cannot help there. Worth stating in the docs rather than pretending otherwise.
