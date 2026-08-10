## Why

This is a fork, and it shares its storage with the project it forked from.

`SettingsFileInfo.DefaultSettingsPath` resolves to `%APPDATA%\mRemoteNG`, and
`ConnectionsFileInfo.DefaultConnectionsFile` is `confCons.xml` — the same directory and the same
filename upstream mRemoteNG uses. A user who installs this fork alongside upstream, or who tries it
and goes back, is pointing two applications at one file.

Four of the security changes proposed against [mRemoteNG#3416](https://github.com/mRemoteNG/mRemoteNG/issues/3416)
alter that file or the shared SQL store in ways upstream cannot read:

| Change | What upstream does with it |
|---|---|
| `harden-connection-file-kdf` | Ignores the unknown `KdfPrf` attribute, derives with SHA-1, fails to decrypt — and reports a wrong password |
| `replace-default-connection-file-key` | Does not recognise the third sentinel |
| `encrypt-sql-backend-with-aead` | Reads AEAD ciphertext with the legacy provider, or refuses on `ConfVersion` |
| `require-sql-master-password` | Rides on the above |

As drafted, `harden-connection-file-kdf` applies on the next save with no prompt at all. So merely
opening and saving in this fork would migrate a user's store into a format the application they came
from can no longer read — with no warning, and no way back.

**That is a lock-in produced as a side effect of a security fix, and it is not ours to impose.** A
user evaluating a fork must be able to stop evaluating it. Leaving a known weakness that a user can
choose to fix is a better failure than silently taking away their ability to leave.

The same argument applies to a shared SQL database, where the consequence lands on colleagues who
never installed the fork at all.

## What Changes

- A store — connection file or SQL database — has a **format level**: `Classic`, which upstream
  mRemoteNG can read, or `Hardened`, which it cannot.
- **`Classic` is the default and nothing upgrades a store without being asked.** Every hardening
  change gates on the level rather than applying on next save.
- Raising the level is **one** explicit confirmation covering all of the hardening at once, not one
  prompt per change. It names the consequence in the user's terms: upstream mRemoteNG, and older
  builds of this fork, will no longer open this store.
- **Export in classic format** is the way back: a file upstream can read, produced from a hardened
  store at any time. It must exist before the upgrade is offered.
- A `Hardened` store is never produced where a `Classic` one was expected — including by import,
  by save-as, and by the automatic backup path.

### What this costs

A user who never opts in gets none of the hardening. `harden-connection-file-kdf` was the cheapest
change of the seven precisely because it needed no migration, and gating it makes it a decision
rather than a default.

That is the trade being made deliberately. The alternative is a fork that improves your security by
removing your ability to go back to the software you actually chose, without telling you. Between a
weakness the user can fix on request and a lock-in they cannot undo, the weakness is the better
default.

## Capabilities

Adds `storage-format-compatibility`. It owns the level, the single confirmation, and the promise that
a `Classic` store stays readable by upstream — a concern that spans the connection file and the SQL
database, and belongs to neither `connection-file-encryption` nor `sql-backend-encryption` alone.

## Impact

Every hardening change becomes dependent on this one:

- `harden-connection-file-kdf` — writes `KdfPrf` only at `Hardened`
- `replace-default-connection-file-key` — writes the per-file key only at `Hardened`; its migration
  prompt is folded into the single confirmation here
- `encrypt-sql-backend-with-aead` — its `ConfVersion` gate becomes this level for the SQL store
- `require-sql-master-password` — rides on that gate

`retire-legacy-rijndael-for-settings`, `scope-diagnostic-logging` and
`narrow-connection-password-exposure` are unaffected: settings and logs are not shared storage in the
same sense, and neither change alters a file format.

Touches `mRemoteNG/Config/Serializers/ConnectionSerializers/Xml/XmlRootNodeSerializer.cs`,
`mRemoteNG/Config/Serializers/XmlConnectionsDecryptor.cs`,
`mRemoteNG/Security/Factories/CryptoProviderFactoryFromXml.cs`,
`mRemoteNG/Config/Connections/SqlConnectionsSaver.cs`,
`mRemoteNG/Config/Connections/SqlDatabaseVersionVerifier.cs`, and a new options surface.

The risk is that the level becomes a thing users never find, so the hardening ships and nobody gets
it. That argues for stating the store's level somewhere permanently visible rather than burying it in
options, and for making the classic export prominent enough that upgrading feels reversible — which
it is, and which is the whole point.
