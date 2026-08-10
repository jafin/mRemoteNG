# Tasks

Depends on `add-storage-format-opt-in`. The new PRF is written only at the hardened level; applying
it on next save would lock users out of upstream mRemoteNG, which reads the same file from the same
path.

## 1. Key derivation function

- [ ] 1.1 Give `Pkcs5S2KeyGenerator` a `HashAlgorithmName` parameter and pass it to `Rfc2898DeriveBytes.Pbkdf2` in place of the hardcoded `HashAlgorithmName.SHA1` (`Pkcs5S2KeyGenerator.cs:32`).
- [ ] 1.2 Remove the `iterations = 1000` default from the constructor. No caller relies on it; both real construction sites pass the configured value.
- [ ] 1.3 Reject a function outside the supported set rather than deriving with something unreadable by a later version.
- [ ] 1.4 Tests: SHA-1 derivation still matches the vectors the current code produces, byte for byte — this is what keeps existing files readable; SHA-256 produces a different key for the same inputs; construction without an iteration count does not compile.

## 2. Provider

- [ ] 2.1 Add the PRF to `AeadCryptographyProvider` alongside `KeyDerivationIterations`, defaulting to SHA-256 for new instances.
- [ ] 2.2 Include the PRF in both KDF cache keys — `_cachedEncrypt*` and `_cachedDecrypt*` — so a file opened under one function and saved under another does not reuse a key across them.
- [ ] 2.3 Tests: two derivations differing only by function do not share a cached key; the existing single-derivation-per-save behaviour is unchanged when the function is constant.

## 3. File format

- [ ] 3.1 Write a `KdfPrf` root attribute in `XmlRootNodeSerializer` beside `KdfIterations` (`XmlRootNodeSerializer.cs:22`) — **only at the hardened level**. A classic store must come out byte-compatible with what upstream writes.
- [ ] 3.2 Write it in `XmlCredentialPasswordEncryptorDecorator` too (`XmlCredentialPasswordEncryptorDecorator.cs:53`), which maintains its own copy of the same attribute.
- [ ] 3.3 Read it in `CryptoProviderFactoryFromXml` (`CryptoProviderFactoryFromXml.cs:40`), treating a missing or unparseable attribute as SHA-1.
- [ ] 3.4 Tests: a file with `KdfPrf="SHA256"` round-trips; a file with no attribute decrypts as SHA-1; a file with a nonsense attribute value decrypts as SHA-1 rather than throwing.

## 4. Compatibility

- [ ] 4.1 Fixture test: decrypt a connection file captured from v1.82.0 before this change, with no attribute present, and confirm every password comes back. This is the regression that matters — everything else in this change is additive.
- [ ] 4.2 Fixture test: the same for a credential file written by `XmlCredentialPasswordEncryptorDecorator`.
- [ ] 4.3 Confirm the SQL path is untouched. It uses `LegacyRijndaelCryptographyProvider` and does not reach this code; `encrypt-sql-backend-with-aead` is where that moves.

## 5. Verification

- [ ] 5.1 Full build; zero new analyzer warnings.
- [ ] 5.2 Full test suite; zero failures, no `[Ignore]`.
- [ ] 5.3 `openspec validate harden-connection-file-kdf --strict`.
- [ ] 5.4 Manual: open a connection file written by the current release, confirm connections decrypt, save at the classic level, confirm **no** attribute appears and an upstream mRemoteNG build still opens it.
- [ ] 5.5 Manual: raise the level, save, confirm the attribute appears and the file reopens here.
- [ ] 5.6 Manual: set a master password on a migrated file, close, reopen, confirm it is still accepted.
- [ ] 5.7 Manual: measure file-open time before and after on a file with 200 connections. The PRF change should not move it; a regression here means the cache keys are wrong and the KDF is running per field.
