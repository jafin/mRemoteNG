## Why

The SQL backend encrypts connection passwords with a key that is an unsalted MD5 of the master
password, under AES-CBC with no authentication tag:

```csharp
byte[] key = MD5.HashData(Encoding.UTF8.GetBytes(encryptionKey.ConvertToUnsecureString()));
```

`LegacyRijndaelCryptographyProvider.cs:40` (and `:76` on the decrypt side)

This is the **write** path, not a legacy read path. `SqlConnectionsSaver.UpdateConnectionsTable`
constructs the provider (`SqlConnectionsSaver.cs:153`) and hands it to `DataTableSerializer`, which
encrypts the `Password`, `RDGatewayPassword` and `VNCProxyPassword` columns with it
(`DataTableSerializer.cs:673-675`). `ConnectionsService.cs:309` injects the same provider into the
loader.

So a team on the SQL backend has its connection passwords protected by a single MD5 with no salt and
no iterations — recoverable at GPU speed — while the XML backend on the same build uses AES-256-GCM
with PBKDF2 at 600,000 iterations. The two backends are not close to equivalent, and nothing in the
UI says so.

The missing authentication tag is the second defect. Anyone with write access to the connections
table can flip ciphertext bits in a `Password` column and the client will decrypt whatever falls out
without noticing, which for CBC is also the shape of a padding oracle.

Raised as M-2 in the upstream security audit ([mRemoteNG#3416](https://github.com/mRemoteNG/mRemoteNG/issues/3416)).
Applies to our fork exactly as written.

## What Changes

- The SQL backend encrypts with `AeadCryptographyProvider` — AES-256-GCM, PBKDF2, per-record salt and
  nonce — the same provider the XML backend already uses.
- Which provider is used follows the database's `ConfVersion`, the version marker the schema already
  carries and `SqlDatabaseVersionVerifier` already enforces. Below the new version, legacy; at or
  above it, AEAD.
- A database at the old version stays **fully readable and writable** with the legacy provider, and
  says so once per session. The upgrade is deliberate and explicit, never a side effect of a save.
  **This reverses the original wording, "is read and is not written", after implementing it.** That
  refusal was a bigger hammer than this proposal's own reasoning called for: what design.md
  establishes is that the *upgrade* must not be prompted, because it decides for a whole team and
  cannot be undone — which says nothing about whether a legacy database should keep accepting
  writes. Refusing would not improve the weak encryption these databases are already in; it would
  only stop people working until an administrator acted, and because saves here are automatic and
  debounced it would surface as an error on every rename. It would also contradict this fork's own
  precedent, where a classic connection file stays fully writable and hardening is offered rather
  than imposed. The cost of the softer rule is stated plainly: a team that never opens the SQL
  options page keeps the weak format indefinitely.
- Upgrading re-encrypts every stored secret in one transaction and raises `ConfVersion`.
- `LegacyRijndaelCryptographyProvider` keeps its decrypt path for the SQL backend and loses its
  encrypt callers there.

## Capabilities

Adds `sql-backend-encryption`. The requirements are about which provider protects the database and
how a shared database moves between the two, which is not a concern the XML capability has —
`connection-file-encryption` describes a file one user owns, this describes a store several clients
share.

## Impact

`mRemoteNG/Config/Connections/SqlConnectionsSaver.cs`,
`mRemoteNG/Config/Connections/SqlConnectionsLoader.cs`,
`mRemoteNG/Connection/ConnectionsService.cs`,
`mRemoteNG/Config/Serializers/ConnectionSerializers/Sql/SqlDatabaseMetaDataRetriever.cs`,
`mRemoteNG/Config/Serializers/ConnectionSerializers/Sql/DataTableSerializer.cs` and its deserializer,
`mRemoteNG/Config/Connections/SqlDatabaseVersionVerifier.cs`.

**The multi-client sequencing is the whole risk.** A SQL database is shared. The moment one client
upgrades it, every client still on an older build reads ciphertext it cannot decrypt — and the older
builds have no version check that would explain why, so users see empty or corrupt passwords rather
than a message. The upgrade must therefore be an explicit administrative action with a warning that
names the consequence, never automatic, and `SqlDatabaseVersionVerifier` must refuse a database
newer than the running build rather than reading it badly.

**"Older clients" includes upstream mRemoteNG.** This is a fork, and a team's SQL database is
routinely reached by people running the software this fork came from. The `ConfVersion` gate is the
SQL store's format level in the sense `add-storage-format-opt-in` defines, and the warning must name
upstream explicitly rather than saying "older clients" — the person confirming the upgrade is
deciding for colleagues who never installed this fork and cannot undo it.

Any client that must keep the old format keeps working as long as nobody upgrades. That is the
property that makes a staged rollout possible, and it is why the change gates on `ConfVersion` rather
than sniffing the ciphertext.
