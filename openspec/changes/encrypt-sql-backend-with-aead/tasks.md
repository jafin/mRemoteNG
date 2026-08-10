# Tasks

**This proposal is split across releases.** Section 1 ships a release ahead of the rest; sections 2
onward wait for `add-storage-format-opt-in`. See
[SECURITY-AUDIT-3416.md](../SECURITY-AUDIT-3416.md) for the full order.

## 1. Version refusal — ships first

- [ ] 1.1 In `SqlDatabaseVersionVerifier`, treat a database version newer than the build as a refusal that names both versions, instead of proceeding to read rows.
- [ ] 1.2 Tests: a newer version loads nothing and reports both numbers; the current version still loads; an older version still loads.
- [ ] 1.3 Release this ahead of the rest. Until every client refuses a newer database, upgrading one produces silent garbage on the others rather than a message — see design.md, "Refuse a database newer than the build".

## 2. Provider selection

- [ ] 2.1 Add a version constant for authenticated encryption and a helper that returns the provider for a given `ConfVersion`, mirroring `CryptoProviderFactoryFromXml`'s role on the XML side.
- [ ] 2.2 Replace the hardcoded `new LegacyRijndaelCryptographyProvider()` in `ConnectionsService.cs:309` with the helper, fed by the metadata the loader already retrieves.
- [ ] 2.3 Replace the hardcoded provider in `SqlConnectionsSaver.cs:153` the same way.
- [ ] 2.4 Check `SqlDatabaseMetaDataRetriever.cs:96`, which builds its own legacy provider, and route it through the helper or document why its use is version-independent.
- [ ] 2.5 Tests: the helper returns legacy below the version and AEAD at or above it; the loader and saver agree for a given version.

## 3. Refuse to write a legacy database

- [ ] 3.1 Make `SqlConnectionsSaver` refuse when the database is below the new version, with a message naming the upgrade.
- [ ] 3.2 Confirm the refusal surfaces where a user will see it rather than only in the log — a save that silently does nothing is worse than the weak encryption it avoids.
- [ ] 3.3 Tests: a save against a legacy database does not write and reports; a save against an upgraded database writes AEAD ciphertext.

## 4. Upgrade

- [ ] 4.1 Put the upgrade in the SQL options page and **do not prompt for it** when a database is opened — decided, see design.md. Deliberately not the once-per-store offer `add-storage-format-opt-in` defines: that suits the connection file, where the person prompted is the person affected. Here the decision is team-wide and belongs to whoever administers the database.
- [ ] 4.2 Authenticate the master password against the `Protected` metadata before touching a row.
- [ ] 4.3 Read every secret column with the legacy provider, rewrite with AEAD, raise `ConfVersion`, all in one `DbTransaction`.
- [ ] 4.4 Warn before proceeding and require confirmation. Name upstream mRemoteNG among the clients that will stop reading the database, and say that this decides for colleagues who never installed this fork — see `add-storage-format-opt-in`.
- [ ] 4.5 Tests: a successful upgrade re-encrypts every secret and raises the version; a failure mid-way rolls back to a fully legacy database; a wrong password modifies nothing.

## 5. Serializers

- [ ] 5.1 Confirm `DataTableSerializer` and `DataTableDeserializer` are provider-agnostic — they take `ICryptographyProvider`, so this should be a check rather than a change.
- [ ] 5.2 Check the commented-out block at `DataTableSerializer.cs:670-672` against the live code at `:673-675`; if it is a stale `SecureString` variant, remove it rather than leaving two versions of the comparison.
- [ ] 5.3 Tests: round-trip every secret column through both providers.

## 6. Verification

- [ ] 6.1 Full build; zero new analyzer warnings.
- [ ] 6.2 Full test suite; zero failures, no `[Ignore]`.
- [ ] 6.3 `openspec validate encrypt-sql-backend-with-aead --strict`.
- [ ] 6.4 Manual, against a real SQL Server: load a legacy database, confirm connections decrypt and a save is refused.
- [ ] 6.5 Manual: upgrade it, confirm connections still decrypt, confirm the stored ciphertext changed shape, confirm a save now succeeds.
- [ ] 6.6 Manual: point a build from before task 1 at the upgraded database and record what it does. This is the failure the whole sequencing exists to prevent, and it is worth seeing once.
- [ ] 6.7 Manual: alter a `Password` value in the database directly and confirm the client reports a decryption failure rather than returning a wrong value.
