## Why

A SQL database with no master password is encrypted under `mR3m`, and nothing tells anyone.

`SqlConnectionsLoader.GetDecryptionKey` (`SqlConnectionsLoader.cs:83-95`) obtains its key one of two
ways, and both end at the published constant:

```csharp
if (string.IsNullOrEmpty(cipherText))
    return new RootNodeInfo(RootNodeType.Connection).DefaultPassword.ConvertToSecureString();

PasswordAuthenticator authenticator = new(_cryptographyProvider, cipherText, () => AuthenticationRequestor(""));
bool authenticated = authenticator.Authenticate(new RootNodeInfo(RootNodeType.Connection).DefaultPassword.ConvertToSecureString());
```

The writing side matches: `SqlDatabaseMetaDataRetriever.WriteDatabaseMetaData` encrypts the sentinel
with `Runtime.EncryptionKey` when the root node has no password, and `Runtime.EncryptionKey` is
initialised from `RootNodeInfo.PasswordString` (`Runtime.cs:66-67`) — which is `mR3m` unless a custom
password was set.

So every connection password in a shared team database is protected by four characters in a public
repository, recoverable by anyone with `SELECT` on the connections table. `replace-default-connection-file-key`
fixes this for the XML file with a DPAPI-wrapped per-file key; **that answer cannot be transplanted
here**, because a key bound to one Windows account is useless to a store several people read. The SQL
backend needs a different one, which is why it was carved out of that proposal rather than bolted on.

### A second defect in the same method

The SQL path never checks what it decrypted. `PasswordAuthenticator.Authenticate`
(`Security/Authentication/PasswordAuthenticator.cs:36-40`) treats "`Decrypt` did not throw" as
success. Against AES-CBC with PKCS7 and no authentication tag, a wrong key produces valid padding
roughly once in 256 attempts, and the method's own retry loop supplies the attempts.

The practical impact is modest: passing the check yields a wrong key, so passwords still fail to
decrypt. It yields the rest — hostnames, usernames, ports, the shape of the estate — which are not
encrypted at all. It is also a small fix and there is no reason to carry it.

**Correction, found while implementing.** This proposal originally said the XML path does not have
the problem, because `XmlConnectionsDecryptor.ConnectionsFileIsAuthentic` (`XmlConnectionsDecryptor.cs:145`)
compares the plaintext to `"ThisIsNotProtected"`. That is true only of its *unprotected* branch. When
the file has a master password, the comparison fails and it falls through to
`XmlConnectionsDecryptor.Authenticate` (`:156`), which uses the same `PasswordAuthenticator` and
inherits the same weakness.

XML is narrower in practice: modern files use `AeadCryptographyProvider`, where a wrong key fails the
GCM tag deterministically. The gap is real only for legacy-provider files — those written before AEAD
and still opened with a master password — which `CryptoProviderFactoryFromXml` still selects the
legacy provider for.

This change fixes the SQL caller, which is what it scoped. The XML master-password branch is left as
found: correcting it changes behaviour on the XML path, which task 1.2 was written to protect, and
that reversal is worth deciding rather than absorbing. The mechanism added here — an optional
plaintext validator on `PasswordAuthenticator` — is what a follow-up would use, so nothing has to be
rebuilt for it.

## What Changes

- At the authenticated-encryption version introduced by `encrypt-sql-backend-with-aead`, a SQL
  database **requires** a master password. There is no default-key fallback at that version.
- Databases below that version keep reading under `mR3m` exactly as they do now. Nothing becomes
  unreadable.
- The upgrade already prompts for the master password in order to re-encrypt, so requiring one costs
  the administrator no extra step — it removes an option rather than adding work.
- The sentinel is verified by comparing the decrypted plaintext, matching the XML path. This part
  depends on nothing and can ship immediately.
- A database still on the default key says so in the UI instead of looking encrypted.

## Capabilities

Modifies `sql-backend-encryption`, introduced by `encrypt-sql-backend-with-aead` and not yet
archived. That change decides which cipher protects the store; this one decides what the key is.
Ordering them the other way would mean requiring a master password and then still deriving from it
with an unsalted MD5.

## Impact

`mRemoteNG/Config/Connections/SqlConnectionsLoader.cs`,
`mRemoteNG/Config/Serializers/ConnectionSerializers/Sql/SqlDatabaseMetaDataRetriever.cs`,
`mRemoteNG/Security/Authentication/PasswordAuthenticator.cs`,
`mRemoteNG/UI/Forms/OptionsPages/SqlServerPage.cs`.

The cost lands on administrators, not on the code. **Every user of a shared database must be given
the master password before it is upgraded**, and mRemoteNG has no way to distribute one. Requiring
the password without saying this turns an upgrade into an outage for everyone who was not told.

There is also no recovery. A forgotten master password on a SQL store means the connections are gone,
where today they are recoverable precisely because the key is public — the weakness is, from one
angle, the backup. That trade has to be stated where the administrator sets the password, not
discovered later.

`PasswordAuthenticator` is shared with the XML path, so the sentinel change must be made where the
SQL caller can verify its own plaintext without altering what the XML caller already does correctly.
