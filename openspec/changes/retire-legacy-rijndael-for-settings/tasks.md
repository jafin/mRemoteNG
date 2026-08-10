# Tasks

## 1. Shared resolver

- [ ] 1.1 Add a component that encrypts and decrypts a settings secret, choosing the provider from the value's marker on read and always using authenticated encryption on write.
- [ ] 1.2 Define the marker so an unmarked value is unambiguously legacy and an unrecognised marker fails rather than falling back.
- [ ] 1.3 Tests: unmarked reads legacy; marked reads AEAD; unknown marker fails without attempting legacy; writes always produce a marked value.

## 2. Call sites

Eleven sites, each replacing an inline `new LegacyRijndaelCryptographyProvider()` with the resolver.
Grouped so a partial landing still leaves each secret consistent between its reader and its writer.

- [ ] 2.1 Default credential password: `CredentialsPage.cs:58,81` (write), `RdpProtocol.cs:1144` and `ExternalToolArgumentParser.cs:219` (read). All four together — this secret has three readers and one writer, and splitting them breaks it.
- [ ] 2.2 SQL Server password: `SqlServerPage.cs:129,161`, `DatabaseConnectorFactory.cs:25`.
- [ ] 2.3 Update proxy password: `AppUpdater.cs:52`.
- [ ] 2.4 SSH secret: `SshCredentialResolver.cs:153`.
- [ ] 2.5 `SecurityPage.cs:172` — establish what this one encrypts before changing it; it is the least obvious of the set.
- [ ] 2.6 Tests per group: a value written by the new code is read back by every reader of that secret.

## 3. Registry provisioning

- [ ] 3.1 `OptRegistryCredentialsPage.cs:146`, `OptRegistrySqlServerPage.cs:144`, `OptRegistryUpdatesPage.cs:200` decrypt values an administrator provisions. Establish what an administrator is documented to put there before touching these.
- [ ] 3.2 Most likely outcome: these stay decrypt-only on the legacy provider, since a documented provisioning format is an external contract even when the file is ours. If so, say that in the code — an untouched legacy call site with no explanation reads as an oversight.
- [ ] 3.3 Tests: provisioning through the registry still works exactly as documented.

## 4. Close the door

- [ ] 4.1 Confirm no settings write path constructs `LegacyRijndaelCryptographyProvider` directly. A test asserting this is worth more than the review that finds it once.
- [ ] 4.2 Leave the SQL backend's use alone — `encrypt-sql-backend-with-aead` owns it, and touching both from two changes is how one of them gets half done.
- [ ] 4.3 Assert in a test that no settings secret is protected by `ProtectedData`. `replace-default-connection-file-key` introduces that protector for the connection file and extending it here would break the portable edition on the second machine it reached — silently, and in code paths with nowhere to prompt.

## 5. Verification

- [ ] 5.1 Full build; zero new analyzer warnings.
- [ ] 5.2 Full test suite; zero failures, no `[Ignore]`.
- [ ] 5.3 `openspec validate retire-legacy-rijndael-for-settings --strict`.
- [ ] 5.4 Manual: with settings written by the current release, confirm each of the six secrets still works — default credentials on an RDP connection, a SQL Server connection, an update check through an authenticated proxy, an SSH connection, an external tool using the password token.
- [ ] 5.5 Manual: re-save each secret, confirm the stored value changes shape, confirm it still works.
- [ ] 5.6 Manual: corrupt one migrated value in the settings file and confirm it reports a failure rather than returning a wrong password to a connection.
