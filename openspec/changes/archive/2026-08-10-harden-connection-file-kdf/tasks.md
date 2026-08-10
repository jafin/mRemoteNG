# Tasks

Depends on `add-storage-format-opt-in`. The new PRF is written only at the hardened level; applying
it on next save would lock users out of upstream mRemoteNG, which reads the same file from the same
path.

## 1. Key derivation function

- [x] 1.1 Give `Pkcs5S2KeyGenerator` a `HashAlgorithmName` parameter. — **Deviation: the default is SHA-1, not SHA-256.** The proposal had it the other way round, which is the unsafe direction: any path that forgets to set it would produce a file upstream cannot read. SHA-1 defaulting means forgetting produces a compatible file, and only the hardened path opts in.
- [x] 1.2 Remove the `iterations = 1000` default from the constructor. — Nine existing tests relied on it and had to be given explicit values, which is the point: a defaulted iteration count is indistinguishable at the call site from a chosen one.
- [x] 1.3 Reject a function outside the supported set. — `KeyDerivationPrf.IsSupported`; MD5 throws.
- [x] 1.4 Tests: `DerivationMatchesTheStandardVectors` pins both functions against RFC 2898 output computed independently, so the SHA-1 row is fixed by the standard rather than by us. Construction without an iteration count no longer compiles — which is how the nine test updates in 1.2 were found.

## 2. Provider

- [x] 2.1 Add the PRF alongside `KeyDerivationIterations`, defaulting to **SHA-1** — see 1.1. Added to `ICryptographyProvider`, following the precedent that `KeyDerivationIterations` is already there and ignored by providers that derive no key.
- [x] 2.2 Include the PRF in both KDF cache keys.
- [x] 2.3 Tests: covered by `DifferingFunctionsProduceDifferingKeys` and the round-trip tests, which would fail on a stale cached key.

## 3. File format

- [x] 3.1 Write a `KdfPrf` root attribute beside `KdfIterations`, only when the function is not SHA-1. `XmlConnectionsSaver` sets the function from the store's level — the one place that knows both.
- [x] 3.2 **Not written — the credential file stays classic permanently.** Decided rather than deferred. It has no storage format level, so there is nowhere to record a choice and no way for a user to make one; hardening it would break upstream mRemoteNG unconditionally rather than on request. Pinned in the decorator's constructor rather than left to the caller, because the provider is supplied from outside and a caller configured for the connection file would otherwise harden it by accident.
- [x] 3.3 Read it. — **The proposal named the wrong reader.** `CryptoProviderFactoryFromXml` serves the *credential* file; the connection file builds its provider in `XmlConnectionsDecryptor` via `XmlConnectionsDeserializer.CreateDecryptor`. Both now read it. `XmlConnectionsDecryptor.CreateThreadLocalProvider` needed it too, or batch decryption would silently fall back to SHA-1.
- [x] 3.4 Tests: `KeyDerivationPrfTests` plus round-trip tests in `XmlSerializationLifeCycleTests`. The hardened round-trip is what caught the wrong-reader error in 3.3.

## 4. Compatibility

- [x] 4.1 `AFileWithNoRecordedFunctionStillDecrypts` and `AClassicStoreWritesNoKdfPrfAttribute` cover the shape. A captured-file fixture would be stronger and is worth adding when one is to hand; the manual check in 5.4 is the real version of it.
- [x] 4.2 Tests instead of a fixture: `TheCredentialFileStaysClassic` asserts the pin survives a provider configured otherwise, and `TheCredentialFileRecordsNoPseudoRandomFunction` asserts the attribute never reaches the file.
- [x] 4.3 Confirm the SQL path is untouched — it uses the legacy provider and never reaches this code.

## 5. Verification

- [x] 5.1 Full build; zero new analyzer warnings.
- [x] 5.2 Full test suite; zero failures, no `[Ignore]`. — 7394 passed; 7600 after merging `dev`.
- [x] 5.3 `openspec validate harden-connection-file-kdf --strict`.
- [x] 5.4 Manual: open a connection file written by the current release, confirm connections decrypt, save at the classic level, confirm **no** attribute appears and an upstream mRemoteNG build still opens it. — Passed.
- [x] 5.5 Manual: raise the level, save, confirm the attribute appears and the file reopens here. — Passed. **The level has no user interface yet**, so raising it meant hand-editing `StorageFormat="Hardened"` onto the root element. Safe to do by hand because the two attributes are read independently: derivation reads `KdfPrf` (absent means SHA-1), so a file carrying the level but not yet the function still opens, and the level only decides what the next save writes. That is the same sequence the deferred confirmation in §4/§5 of `add-storage-format-opt-in` will perform once it exists.
- [x] 5.6 Manual: set a master password on a migrated file, close, reopen, confirm it is still accepted. — Passed.
- [x] 5.7 Manual: measure file-open time before and after on a file with 200 connections. The PRF change should not move it; a regression here means the cache keys are wrong and the KDF is running per field. — Passed; no movement, so the derived key is cached once as intended.

## Carried in with this change — done

**The deferred confirmation and offer from `add-storage-format-opt-in` §4 and §5.** They belong here,
because this is the first change that makes the warning true — a hardened file now genuinely does not
open in upstream mRemoteNG.

Both shipped on this branch and are recorded against §4 and §5 of that change: one confirmation for
all hardening, worded around applications rather than algorithms; the upgrade offered once per
classic connection file and kept reachable afterwards from File ▸ Storage Format. The claim the
confirmation rests on was checked against a real upstream build — see that change's task 6.7, where
upstream asks for the password again and refuses the correct one, exactly as the wording says.

## Decided during implementation

**The credential file stays classic permanently.** It has no format level and no place to record one,
so the choice was between hardening it unconditionally — exactly what `add-storage-format-opt-in`
exists to prevent — and leaving it alone. It is pinned in code and tested, not merely documented, so
a provider configured for the connection file cannot harden it by accident.
