# Tasks

**This proposal is split across releases.** Section 1 depends on nothing and should ship on its own,
ahead of the crypto migration. Sections 2 onward depend on `encrypt-sql-backend-with-aead` having
landed, since requiring a password is only worth doing once the key derived from it is sound. See
[SECURITY-AUDIT-3416.md](../SECURITY-AUDIT-3416.md) for the full order.

## 1. Verify the sentinel — ships independently

- [ ] 1.1 In `SqlConnectionsLoader.GetDecryptionKey`, require the decrypted sentinel to be `ThisIsProtected` or `ThisIsNotProtected` rather than accepting any decryption that did not throw.
- [ ] 1.2 Make the comparison at the SQL caller, not inside `PasswordAuthenticator`. The XML path already compares its own plaintext (`XmlConnectionsDecryptor.cs:145`) and must keep behaving exactly as it does.
- [ ] 1.3 Confirm a rejected value still consumes an attempt and re-prompts, rather than failing outright — the retry loop is the existing behaviour and users rely on it for typos.
- [ ] 1.4 Tests: the right password authenticates; a wrong password whose decryption happens not to throw is rejected; the attempt limit is unchanged; the XML path is unaffected.
- [ ] 1.5 Test the specific case directly: construct ciphertext and a key that decrypt without throwing to something that is not a sentinel. This is the defect; a test that only exercises right-and-wrong passwords will pass without it.

## 2. Require a password at the new version

- [ ] 2.1 Remove the default-key fallback from `GetDecryptionKey` for databases at the authenticated-encryption version, keeping it for earlier versions.
- [ ] 2.2 Handle the empty-sentinel branch (`SqlConnectionsLoader.cs:87`), which today returns the default key with no check at all. At the new version it means an uninitialised database, not an unprotected one.
- [ ] 2.3 In `WriteDatabaseMetaData`, stop encrypting the unprotected sentinel under `Runtime.EncryptionKey` at the new version; there is no unprotected state to record.
- [ ] 2.4 Tests: an upgraded database with no password loads nothing; with the right password it loads; a legacy database is unaffected in both cases.

## 3. Upgrade path

- [ ] 3.1 Require the master password in the upgrade added by `encrypt-sql-backend-with-aead`, rather than accepting the default key and re-encrypting under it — which would satisfy that change's requirements and leave this one's defect intact.
- [ ] 3.2 Extend the upgrade warning: colleagues must be given the password before the upgrade, and a forgotten password cannot be recovered. This sits alongside the upstream-compatibility warning `encrypt-sql-backend-with-aead` adds — one dialog, both consequences, since they land on the same people.
- [ ] 3.3 Investigate the open question in design.md — whether `tblUpdate` can support telling the administrator how many clients have recently used the database. Drop it if the count cannot be trusted; a confident wrong number is worse than none.
- [ ] 3.4 Tests: the upgrade refuses to proceed on the default key; the warning names both consequences.

## 4. Say so in the interface

- [ ] 4.1 On the SQL Server options page, state when the configured database is using the built-in default key.
- [ ] 4.2 Word it as what it means — stored passwords are not protected — rather than as a version number. An administrator who does not already know what `mR3m` is will not act on "legacy encryption".
- [ ] 4.3 Tests: shown for a default-key database, absent for a password-protected one.

## 5. Verification

- [ ] 5.1 Full build; zero new analyzer warnings.
- [ ] 5.2 Full test suite; zero failures, no `[Ignore]`.
- [ ] 5.3 `openspec validate require-sql-master-password --strict`.
- [ ] 5.4 Manual against a real SQL Server: a legacy database with no password still opens; the warning appears.
- [ ] 5.5 Manual: upgrade with a master password, confirm it opens with the password and refuses without it.
- [ ] 5.6 Manual: attempt the upgrade without setting a password and confirm it is refused with an explanation.
- [ ] 5.7 Manual: a second client opening the upgraded database is prompted, and succeeds with the password.
