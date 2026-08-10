## Why

When no master password is set, the connection file is encrypted under a key derived from a public
constant:

```csharp
public const string LegacyEncryptionKey = "mR3m";
```

`Security/ConnectionFileDefaults.cs:26`, reached through `RootNodeInfo.DefaultPassword`
(`RootNodeInfo.cs:74`) and `SqlConnectionsLoader.cs:86,89`.

This is **CVE-2023-30367**, and it is open in our fork. A `confCons.xml` copied off a machine — from a
backup, a roaming profile, a sync folder, a shared drive — is decryptable by anyone, because the key
is in the source of a public repository. AES-256-GCM at 600,000 PBKDF2 iterations protects nothing
when the password is four characters everybody has.

The default is the case that matters: it is what a user gets who never opens the security options.

Our fork replaced upstream's `//TODO move password away from code to settings` with a doc comment
explaining that changing the value would break every existing file. That is a true statement of the
constraint and not a fix — it is arguably worse, because it reads as a decision rather than a debt.

Raised as H-1 in the upstream security audit ([mRemoteNG#3416](https://github.com/mRemoteNG/mRemoteNG/issues/3416)).

## What Changes

- A connection file with no master password is protected with a random per-file key, wrapped by
  Windows DPAPI at `CurrentUser` scope. The file becomes readable only by the account that wrote it.
- The file states which of the three protections it uses, through the root sentinel it already writes
  to distinguish "protected" from "not protected" (`XmlRootNodeSerializer.cs:41`). A third value is
  added; the two existing ones keep their meaning.
- `mR3m` becomes **read-only**. Existing files open exactly as they do now. Nothing is ever written
  under it again.
- Migration happens on save, once, with a notice explaining what changed and what it costs.
- **The portable edition does not use DPAPI.** A key bound to one Windows account defeats a build
  whose purpose is to run from a USB stick on someone else's machine. Portable is offered a master
  password instead, and if the user declines, it keeps the legacy key with the weakness stated
  plainly rather than silently.

## Capabilities

Modifies `connection-file-encryption`, introduced by `harden-connection-file-kdf` and not yet
archived. That change establishes that the file records its own cryptographic parameters and that
absence keeps its historical meaning; this change adds a parameter of the same kind and depends on
that rule already being in place.

## Impact

`mRemoteNG/Security/ConnectionFileDefaults.cs`,
`mRemoteNG/Tree/Root/RootNodeInfo.cs`,
`mRemoteNG/Config/Serializers/ConnectionSerializers/Xml/XmlRootNodeSerializer.cs`,
`mRemoteNG/Config/Serializers/ConnectionSerializers/Xml/XmlConnectionsDeserializer.cs`,
`mRemoteNG/Config/Serializers/XmlConnectionsDecryptor.cs`,
`mRemoteNG/Security/Factories/CryptoProviderFactoryFromXml.cs`,
`mRemoteNG/Config/Connections/SqlConnectionsLoader.cs`.

This is the highest-severity finding and the one most able to lose a user's data, because it changes
what the file is bound to. Three consequences have to be carried by the design rather than discovered
by users:

- **A DPAPI-protected file does not survive a reinstall or a new machine.** Copying `confCons.xml`
  to a rebuilt PC is something people do. Today it works; after this it does not, unless they export
  first. The migration notice has to say so, and an export path has to exist before the migration is
  offered.
- **Roaming profiles and folder redirection.** DPAPI `CurrentUser` follows the user's master key, not
  the file. Domain users with roaming profiles are usually fine; users whose profile is local but
  whose documents are redirected to a share are not, and they will not know which they are.
- **`SqlConnectionsLoader` uses `DefaultPassword` as the no-master-password fallback** for a shared
  database. A per-user wrapped key is meaningless for a store several people read. The SQL backend
  keeps the legacy default here and needs its own answer — out of scope, noted in design.md.
