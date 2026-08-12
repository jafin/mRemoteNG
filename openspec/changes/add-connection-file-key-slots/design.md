# Design

## Context

`replace-default-connection-file-key` leaves a connection file's root carrying two attributes:

| Attribute | Holds |
|---|---|
| `KeyProtectorMachine` | the file key wrapped by DPAPI at `CurrentUser` scope, absent in the portable edition |
| `KeyProtectorRecovery` | the file key wrapped by a key stretched from the recovery password |

`ConnectionFileKeyProtection.Unwrap` tries the machine protector, then falls back to prompting. One
machine protector means one account opens the file silently.

That change's answer for a shared file is task 5.5: detect a file outside the user profile and write
no machine protector at all, since one that serves a single member of a team costs every other member
a prompt and a misleading message about another account. The result is correct and unsatisfying — a
team is back to a shared password typed at every open, which is roughly where they were before,
except the password is theirs rather than `mR3m`.

## Goals

- Every member of a team opens a shared file without a prompt, after the first time.
- No file written by the preceding change needs migrating.
- Nothing in the file identifies who has opened it.
- Removing a member is possible and is honest about what it requires.

## Non-Goals

- Central key management, directory integration, or escrow. A file on a share has no server to ask.
- Per-slot permissions. Every slot yields the same key; there is no read-only slot and pretending
  otherwise would be worse than not offering it.
- Concurrency control on the shared file. Simultaneous saves already lose data at the whole-file
  level; slots must not make that worse, and need not make it better.

## Decisions

### A list in the existing attribute, not a new element

The root's attributes are read before anything is decrypted, which is what a protector has to be. A
child element cannot be: when `FullFileEncryption` is on, the root's content *is* the ciphertext, so
anything nested there is inside the encrypted region and unreadable at the point the key is needed.

So the slots go in `KeyProtectorMachine` as a separated list. Base64 uses `A–Z a–z 0–9 + / =`, so a
space is unambiguous as a separator and survives XML attribute round-tripping.

The shape this gives is the useful part: **a file with one slot is byte-identical to what the
preceding change writes.** There is no old form and no new form, no version flag on the attribute,
and no migration — a build that knows about slots reads a one-element list, and the concept extends
rather than replaces.

### Unwrapping tries every slot

There is no index and no way to know which slot is yours without attempting it. `ProtectedData.Unprotect`
on a foreign blob throws quickly and asks the user for nothing, so the search is silent and bounded
by team size.

The slot that worked is remembered for the session, alongside the recovery password
(`replace-default-connection-file-key` task 5.6), so the search happens once per run rather than once
per read.

### A slot is added on save, never on open

`replace-default-connection-file-key` decided migration happens on save rather than on load, because
rewriting a file the user only opened turns a read-only inspection into a permanent change. The same
rule applies here, and on a shared file it applies harder: adding a slot on open would make every
open a write to a file other people have open.

The cost is that a member who only ever reads never gets a slot and is prompted every time. That is
the right cost — they are also the member for whom writing to the shared file is least appropriate.

### Slots are unlabelled

Labelling would let a specific slot be removed. It would also mean writing an account identifier for
every member of a team into a file that is on a share by definition, and a hashed identifier is still
an identifier if you have the list of candidates — which, for a team, you do.

The reason it buys so little is the next decision.

### Removing a member is a rekey, and nothing less is honest

Deleting someone's slot does not remove their access. They know the recovery password, and on a
shared file they have very likely had the opportunity to copy the file. The only operation that
actually removes access is a new file key, a new recovery password, and the contents re-encrypted —
after which their copy still opens, but only up to the point they last had.

So rekey is the offered action and slot deletion is not. Rekey drops every slot and writes the
current user's; the remaining members each get prompted once for the new password and re-slot
themselves on their next save, which is exactly the first-open flow they already know.

Stating this in the UI matters more than implementing a slot list. A user who deletes a slot and
believes they have revoked access is worse off than one who was told what revocation costs.

### Losing a slot is self-healing

Two members saving at once loses one of the writes, as it does today for the whole file. If the lost
write carried a slot, that member is prompted for the recovery password on their next open and their
slot is rewritten on their next save.

This is why slots need no locking. The worst outcome of a race is one extra prompt, and the state
converges without anyone doing anything.

## Portable edition

Unchanged: still no machine protector, however many slots the format allows. A portable build runs on
whatever machine it is carried to, so there is no account worth binding a slot to — a slot per
machine visited would grow without bound and serve nobody.

## Risks

| Risk | Mitigation |
|---|---|
| Slot count reveals team size | Accepted, and stated in the proposal. It reveals no identities, which is the part that matters |
| A departed member's slot is assumed to be revocable | Slot deletion is not offered; rekey is, and says what it does |
| Search cost on the open path | Failed DPAPI unwraps are cheap and silent; the successful slot is cached for the session |
| A concurrent save drops a slot | Self-healing — one extra prompt, then rewritten |
| A shared file that nobody can write | A read-only member is prompted every open; they were before this change too |

## Open Questions

- Should a rekey warn that copies made before it still open? It is true of every rekey of every
  system and saying so may be more honest than reassuring. Leaning towards saying it once, in the
  confirmation, rather than in the docs only.
- Is there a case for a slot bound to a certificate rather than to DPAPI? The repository already has
  `CertificateCryptographyProvider` doing RSA-OAEP over an AES-256-GCM session key, and the root
  already records `CertificateThumbprint`. That would give an administrator a way to issue access
  without sharing a password. It is a larger change and belongs on its own, but the slot list is what
  makes it possible at all, which is worth knowing while designing this one.
