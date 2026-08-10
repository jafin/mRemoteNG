## Why

`Pkcs5S2KeyGenerator.DeriveKey` derives the connection file key with PBKDF2 using **HMAC-SHA1**:

```csharp
return Rfc2898DeriveBytes.Pbkdf2(passwordInBytes, salt, _iterations, HashAlgorithmName.SHA1, keyLengthBytes);
```

`Pkcs5S2KeyGenerator.cs:32`

The iteration count is 600,000, set in v1.80.0 in both `AeadCryptographyProvider.KeyDerivationIterations`
(`AeadCryptographyProvider.cs:66`) and `OptionsSecurityPage.settings:15`. **600,000 is OWASP's figure
for PBKDF2-HMAC-SHA256.** Their figure for HMAC-SHA1 is 1,300,000. We adopted the number for one PRF
and kept the other, so the work factor is roughly half what the iteration count was chosen to buy.

Raised as M-1 in the upstream security audit ([mRemoteNG#3416](https://github.com/mRemoteNG/mRemoteNG/issues/3416)).
Two of the three defects it reports are already absent here: the 1000/10000 counts and the
ctor-versus-settings mismatch were fixed in v1.80.0. The PRF was not.

Raising iterations to 1,300,000 instead would more than double the cost of every file open and every
save, on top of a KDF that already dominates both. Changing the PRF costs nothing at runtime and
lands on the figure the iteration count was already chosen against.

## What Changes

- `Pkcs5S2KeyGenerator` takes the PBKDF2 PRF as a parameter instead of hardcoding SHA-1.
- The connection file records its PRF in a root `KdfPrf` attribute, beside the `KdfIterations`
  attribute that already exists for exactly this reason (`XmlRootNodeSerializer.cs:22`,
  `CryptoProviderFactoryFromXml.cs:40`).
- A file with no `KdfPrf` attribute is read as SHA-1. Every file written before this change opens
  unchanged, with no migration step and no user action.
- **`KdfPrf` is written only at the hardened format level** introduced by `add-storage-format-opt-in`.
  A classic store keeps deriving with SHA-1 and stays readable by upstream mRemoteNG.
- `Pkcs5S2KeyGenerator`'s `iterations` parameter loses its `= 1000` default, which no caller uses and
  which would silently produce a 1000-iteration key if one ever did.

### Gated, not automatic

An earlier draft applied the new PRF on the next save of any file. That was wrong for a fork.

This fork writes `%APPDATA%\mRemoteNG\confCons.xml` — upstream mRemoteNG's own path and filename.
Upstream ignores an unknown `KdfPrf` attribute, derives with SHA-1, fails to decrypt, and reports it
as a wrong password. So an automatic write would mean that merely opening and saving in this fork
locks a user out of the application they came from, silently and with no way back.

Gating costs this change its best property — it was the cheapest of the set precisely because it
needed no migration. That is accepted deliberately: see `add-storage-format-opt-in`, which owns the
level, the single confirmation and the classic export.

## Capabilities

Adds `connection-file-encryption`, which no change has owned before. The requirements it introduces —
that the file states its own KDF parameters and that unmarked files keep their historical
interpretation — are the constraint every later change to this format has to respect, including
`replace-default-connection-file-key`.

## Impact

`mRemoteNG/Security/KeyDerivation/Pkcs5S2KeyGenerator.cs`,
`mRemoteNG/Security/SymmetricEncryption/AeadCryptographyProvider.cs`,
`mRemoteNG/Security/Factories/CryptoProviderFactoryFromXml.cs`,
`mRemoteNG/Config/Serializers/ConnectionSerializers/Xml/XmlRootNodeSerializer.cs`,
`mRemoteNG/Config/Serializers/CredentialSerializer/XmlCredentialPasswordEncryptorDecorator.cs`.

The risk is a file written by the new version and opened by an older one. An older build ignores the
unknown `KdfPrf` attribute, derives with SHA-1, and fails to decrypt — reported as a wrong password
rather than as a version problem. This is the same downgrade hazard `KdfIterations` already carries
and is why the change is worth doing once, early, rather than alongside a format change that also
moves the key.

The KDF caches in `AeadCryptographyProvider` key on password, salt and iterations. They must key on
the PRF as well, or a file open followed by a save-as under different parameters reuses the wrong
derived key.
