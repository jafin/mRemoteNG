## Why

`replace-default-connection-file-key` gives a connection file a random key wrapped by two protectors:
one bound to the Windows account, one to a recovery password. That serves one person on any number of
machines. It does not serve several people on one file.

The file carries **one** machine protector. Whoever migrates a shared `confCons.xml` writes their own
DPAPI blob; every other member of the team fails it, falls back to the recovery password on every
open, and has no way to make that stop — there is no slot for a second person. That change therefore
takes the only defensible option available to it and writes **no** machine protector at all for a
file outside the user profile, leaving the whole team on a shared password typed at every open.

Teams sharing a connection file on a share or a synced folder are a normal deployment of this
application, not an edge case. The SQL backend exists for them and many of them do not use it.

The arrangement that fits is the one BitLocker and LUKS use, and it is not exotic: several
protectors, any of which unwraps the same key. Each member's first open uses the recovery password;
their own protector is added; every open after that is silent — for all of them, rather than for
whoever happened to migrate the file.

Not from the upstream audit ([mRemoteNG#3416](https://github.com/mRemoteNG/mRemoteNG/issues/3416)).
It closes a usability regression that the fix for H-1 introduces for shared files.

## What Changes

- The machine protector becomes **a set of slots** rather than a single value. `KeyProtectorMachine`
  holds a separated list of wrapped keys; unwrapping tries each in turn and takes the first that
  succeeds.
- **The existing single-value form is a one-element list**, so no file written by
  `replace-default-connection-file-key` needs migrating and no reader has to tell the two apart.
- A member who opened a file using the recovery password gets **their own slot added on the next
  save**, not on open. Rewriting a file that was only read is the behaviour that change already
  rejected, and on a shared file it would also mean every open is a write.
- A shared file may therefore carry a machine protector again, which **reverses task 5.5 of
  `replace-default-connection-file-key`** for the shared case. The portable edition keeps writing
  none: there the machine is not a stable thing to bind to at all.
- **Slots are unlabelled.** Nothing in the file records whose slot is whose. The alternative is
  putting an account identifier for every member of a team into a file that travels, to support an
  operation — removing one slot — that does not achieve anything on its own.
- **Rekeying is the removal operation.** A new file key, a new recovery password, contents
  re-encrypted, every slot dropped and the current user's re-added. This is what actually removes a
  departed member's access, because they already know the recovery password and hold copies of the
  file. Offered as an explicit action, not implied by anything else.

### Why slots are additive and losing one is harmless

Two members saving a shared file at once is last-writer-wins today, for the whole file. Slots
inherit that and nothing worse: a slot can be lost when one save overwrites another. The member whose
slot vanished is prompted for the recovery password once more, and their slot is written again on
their next save. The failure mode is self-healing and costs one prompt, which is why it does not need
locking to be correct.

## Capabilities

Modifies `connection-file-encryption`. Depends on `replace-default-connection-file-key`, which
introduces the per-file key and both protectors; there is nothing to add slots to before it lands.

## Impact

`mRemoteNG/Security/FileProtection/ConnectionFileKeyProtection.cs`,
`mRemoteNG/Security/FileProtection/DpapiKeyProtector.cs`,
`mRemoteNG/Config/Serializers/ConnectionSerializers/Xml/XmlRootNodeSerializer.cs`,
`mRemoteNG/Config/Serializers/ConnectionSerializers/Xml/XmlConnectionsDeserializer.cs`,
`mRemoteNG/Config/Connections/XmlConnectionsSaver.cs`,
and the connection file's root attributes.

Two consequences to carry rather than discover:

- **A file that grows a slot per member says how many people have opened it.** It says nothing about
  who. That is the cost of not labelling slots and it is the right trade: the alternative writes
  account identifiers into a file whose whole problem is that it travels.
- **Trying every slot costs a DPAPI call per slot on the open path.** A failed `Unprotect` is cheap
  and returns without a prompt, and the count is bounded by the size of a team. It is still work on
  the path that has to stay silent, so the slot that succeeded should be remembered for the session
  rather than re-searched on every read.
