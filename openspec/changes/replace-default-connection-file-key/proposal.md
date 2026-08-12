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

- A connection file is protected by a **random per-file key with two independent protectors**: a
  DPAPI blob at `CurrentUser` scope, and a recovery password. Either unwraps the same key. This is
  the model BitLocker and LUKS use, and it is what makes the rest of this list possible.
- Daily use unwraps through DPAPI and prompts for nothing. The recovery password is typed when the
  file is set up and when it is opened somewhere DPAPI cannot help — another machine, another
  account, a rebuilt profile, a restored backup.
- The file states which protection it uses, through the root sentinel it already writes to
  distinguish "protected" from "not protected" (`XmlRootNodeSerializer.cs:41`). A third value is
  added; the two existing ones keep their meaning.
- `mR3m` becomes **read-only**. Existing files open exactly as they do now. Nothing is ever written
  under it again.
- Migration happens **only at the hardened format level** introduced by `add-storage-format-opt-in`,
  and **requires a recovery password**. A file with only a DPAPI protector is one profile rebuild
  away from being lost, and its backups go with it.
- A classic store keeps the legacy default key and stays readable by upstream mRemoteNG, which reads
  the same `%APPDATA%\mRemoteNG\confCons.xml` this fork writes. The migration prompt this proposal
  originally owned is folded into the single confirmation that change defines — one decision about
  the store, not one per cryptographic property.
- **The portable edition uses the recovery password alone**, with no DPAPI protector. A key bound to
  one Windows account defeats a build whose purpose is to run from a USB stick on someone else's
  machine. Portable therefore produces files the installed edition can also open, and vice versa.
- **A connection file outside the user profile also uses the recovery password alone.** The file
  carries one DPAPI protector, so on a file a team shares it serves exactly one member and costs
  every other one a prompt they have no way to remove. For those users the recovery password is a
  shared secret and the improvement over `mR3m` is that it is theirs rather than published — a real
  improvement and a smaller one than the single-user case gets. Several protectors, one per member,
  is the arrangement that actually fits a team; it is deferred to `add-connection-file-key-slots`
  because it turns a root attribute into a set and needs a revocation story. See design.md,
  "Several people, one file".

### Why two protectors rather than DPAPI alone

An earlier draft of this proposal wrapped the file key with DPAPI only, and offered a manual export
as the escape route. Checking that against the backup feature showed it does not hold.

`FileBackupCreator.CreateBackupFile` is a plain `File.Copy` of the encrypted file
(`FileBackupCreator.cs:29`). Backups are byte-identical, wrapped key included, so a DPAPI-only file
produces backups bound to the same account. Ten rolling backups would all die with the profile —
where today every one of them is recoverable, precisely because the key is public. The change would
have **reduced** recoverability while claiming to improve protection, and done it silently: the copy
succeeds, the file looks normal, and only an attempted restore reveals the loss.

**A sharpening argument this originally made is wrong, and is corrected rather than deleted.** It
claimed `BackupLocation` lets users point backups at a share or synced folder so they outlive the
machine, and that DPAPI defeats that intent. In this fork `BackupLocation` is not a backup
destination at all: it records the *connection file's* path — written by `CommandLineParser`,
`ConnectionsService` and the File menu, read only as a file dialog's initial directory — and
`FileBackupCreator` never consults it. Backups always land beside the connection file, named by
`BackupFileNameFormat`.

The conclusion is untouched, because it never depended on where the backups sit. A rolling set of ten
copies beside the file is exactly as unopenable after a profile rebuild as ten copies on a share.
What the correction removes is a claim about user intent that this codebase does not support.

A second protector costs one prompt at migration and removes the whole class of problem.

## Capabilities

Modifies `connection-file-encryption`, introduced by `harden-connection-file-kdf` and not yet
archived. That change establishes that the file records its own cryptographic parameters and that
absence keeps its historical meaning; this change adds a parameter of the same kind and depends on
that rule already being in place. The recovery password is stretched with the KDF that change
configures, so the two are ordered rather than independent.

## Impact

`mRemoteNG/Security/ConnectionFileDefaults.cs`,
`mRemoteNG/Tree/Root/RootNodeInfo.cs`,
`mRemoteNG/Config/Serializers/ConnectionSerializers/Xml/XmlRootNodeSerializer.cs`,
`mRemoteNG/Config/Serializers/ConnectionSerializers/Xml/XmlConnectionsDeserializer.cs`,
`mRemoteNG/Config/Serializers/XmlConnectionsDecryptor.cs`,
`mRemoteNG/Security/Factories/CryptoProviderFactoryFromXml.cs`,
`mRemoteNG/Config/Connections/SqlConnectionsLoader.cs`,
`mRemoteNG/Config/DataProviders/FileBackupCreator.cs`,
`mRemoteNG/Config/Connections/XmlConnectionsLoader.cs`.

This is the highest-severity finding and the one most able to lose a user's data, because it changes
what the file is bound to. Four consequences have to be carried by the design rather than discovered
by users:

- **Backups are plain copies of the encrypted file.** `FileBackupCreator` is a `File.Copy`, so every
  backup inherits whatever protects the original. The recovery-password protector is what keeps them
  restorable; without it, a rolling set of ten backups is ten copies of the same unopenable file.
  This is the reason the design has two protectors and not one.
- **`XmlConnectionsLoader.TryRecoverFromBackup` will misreport a protector failure.** It walks every
  backup on a load failure and logs a deserialization warning per file. After a profile rebuild all
  of them fail for the same reason, so the user gets ten warnings about corrupt backups when the
  file is intact and only the key is out of reach. It must distinguish "cannot unwrap" from "cannot
  parse" and stop rather than iterate.
- **Roaming profiles and folder redirection.** DPAPI `CurrentUser` follows the user's master key, not
  the file. Domain users with roaming profiles are usually fine; users whose profile is local but
  whose documents are redirected to a share are not, and they will not know which they are. The
  recovery password is what makes this survivable rather than something we must detect perfectly.
- **`SqlConnectionsLoader` uses `DefaultPassword` as the no-master-password fallback** for a shared
  database. A per-user wrapped key is meaningless for a store several people read. The SQL backend
  keeps the legacy default here; `require-sql-master-password` is where that is fixed.
