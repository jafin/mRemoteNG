## Why

`LegacyRijndaelCryptographyProvider` derives its key as an unsalted MD5 of the secret and encrypts
with AES-CBC and no authentication tag (`LegacyRijndaelCryptographyProvider.cs:40,76`). Beyond the
SQL backend, it protects every secret mRemoteNG stores in its own settings:

| Secret | Call site |
|---|---|
| Default credential password | `CredentialsPage.cs:58,81`, read at `RdpProtocol.cs:1144`, `ExternalToolArgumentParser.cs:219` |
| SQL Server password | `SqlServerPage.cs:129,161`, `OptRegistrySqlServerPage.cs:144` |
| Update proxy password | `AppUpdater.cs:52`, `OptRegistryUpdatesPage.cs:200` |
| SSH secret | `SshCredentialResolver.cs:153` |
| Registry-provisioned credentials | `OptRegistryCredentialsPage.cs:146` |

All of them key on `Runtime.EncryptionKey`. Anyone who reads the settings file recovers these at the
cost of one MD5, and can alter any of them undetectably because nothing authenticates the ciphertext.

These values are not connection-file contents, so the constraint that makes the other crypto changes
expensive does not apply: **the settings file is ours, read by no other tool, with no compatibility
promise to anyone.** That makes this the cheapest of the crypto findings to fix properly and the one
with no format negotiation in it.

Raised as L-1 in the upstream security audit ([mRemoteNG#3416](https://github.com/mRemoteNG/mRemoteNG/issues/3416)),
which asks only that write callers be removed. That is the right instinct with the wrong scope: the
audit assumed these were legacy read paths, and they are live write paths for six different secrets.

## What Changes

- Settings secrets are encrypted with `AeadCryptographyProvider` — AES-256-GCM, PBKDF2, per-value
  salt and nonce.
- Ciphertext carries a version prefix so the reader knows which provider produced it. Values with no
  prefix are legacy and decrypt as they do today.
- Values migrate as they are rewritten. Nothing needs a migration pass and nothing breaks on upgrade.
- One helper owns the choice, so a seventh secret added later cannot quietly pick the wrong provider —
  which is how these six accumulated.
- `LegacyRijndaelCryptographyProvider` keeps its decrypt path and loses its settings write callers.
- **Settings secrets stay machine-portable.** They keep deriving from `Runtime.EncryptionKey` and do
  not gain the machine-bound protector `replace-default-connection-file-key` introduces for the
  connection file. See below.

### Settings secrets must not become machine-bound

`replace-default-connection-file-key` protects the connection file with a per-file key wrapped by
DPAPI. It would be a natural-looking next step to do the same here, and it would break the portable
edition silently.

The portable edition carries its settings on the stick. The SQL Server password
(`SqlServerPage.cs:129,161`), the default credential password and the update proxy password all
travel with it, and a DPAPI-wrapped protector cannot be unwrapped on the next machine. Unlike the
connection file, there is nowhere natural to prompt for a recovery password — these values are read
during startup and during a connection attempt, not at a point where a dialog belongs.

So this change improves the cipher and the KDF and deliberately leaves the key where it is. That
leaves `Runtime.EncryptionKey`, which is `mR3m`-derived unless a master password is set — a real
remaining weakness, and a smaller one than an unsalted MD5. Fixing it properly means binding settings
secrets to something the user supplies, which is a separate change with its own portability problem
to solve.

## Capabilities

Adds `settings-secret-encryption`. Distinct from `connection-file-encryption` and
`sql-backend-encryption`: those describe stores with an external format contract and a migration
problem, this describes values only mRemoteNG reads, where the answer is simply to stop writing the
weak form.

## Impact

`mRemoteNG/UI/Forms/OptionsPages/CredentialsPage.cs`,
`mRemoteNG/UI/Forms/OptionsPages/SqlServerPage.cs`,
`mRemoteNG/UI/Forms/OptionsPages/SecurityPage.cs`,
`mRemoteNG/App/Checks/AppUpdater.cs`,
`mRemoteNG/Tools/ExternalToolArgumentParser.cs`,
`mRemoteNG/Connection/Protocol/RDP/RdpProtocol.cs`,
`mRemoteNG/Security/Ssh/SshCredentialResolver.cs`,
`mRemoteNG/Config/Settings/Registry/OptRegistryCredentialsPage.cs`,
`mRemoteNG/Config/Settings/Registry/OptRegistrySqlServerPage.cs`,
`mRemoteNG/Config/Settings/Registry/OptRegistryUpdatesPage.cs`,
`mRemoteNG/Config/DatabaseConnectors/DatabaseConnectorFactory.cs`.

Eleven files, each doing the same two-line thing, which is the risk: it is a wide, shallow change
where a missed call site leaves a value written by one provider and read by another. The version
prefix makes that failure loud rather than silent — an unprefixed value read as AEAD fails cleanly
instead of returning plausible bytes.

The registry-provisioned pages are the subtle ones. `OptRegistryCredentialsPage.cs:146` decrypts a
value an administrator put in the registry, and its comment notes the registry value is not
encrypted at all. Whatever this change does there must not make a documented provisioning path stop
working — most likely those stay on decrypt-only.
