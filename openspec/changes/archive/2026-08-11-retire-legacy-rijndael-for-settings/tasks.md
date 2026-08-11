# Tasks

## 1. Shared resolver

- [x] 1.1 Add a component that encrypts and decrypts a settings secret, choosing the provider from the value's marker on read and always using authenticated encryption on write.
- [x] 1.2 Define the marker so an unmarked value is unambiguously legacy and an unrecognised marker fails rather than falling back.
- [x] 1.3 Tests: unmarked reads legacy; marked reads AEAD; unknown marker fails without attempting legacy; writes always produce a marked value.

## 2. Call sites

Eleven sites, each replacing an inline `new LegacyRijndaelCryptographyProvider()` with the resolver.
Grouped so a partial landing still leaves each secret consistent between its reader and its writer.

- [x] 2.1 Default credential password: `CredentialsPage.cs:58,81` (write), `RdpProtocol.cs:1144` and `ExternalToolArgumentParser.cs:219` (read). All four together — this secret has three readers and one writer, and splitting them breaks it.
- [x] 2.2 SQL Server password: `SqlServerPage.cs:129,161`, `DatabaseConnectorFactory.cs:25`.
- [x] 2.3 Update proxy password: `AppUpdater.cs:52`.
- [x] 2.4 SSH secret: `SshCredentialResolver.cs:153`.
- [x] 2.5 `SecurityPage.cs:172` — establish what this one encrypts before changing it; it is the least obvious of the set.
- [x] 2.6 Tests per group: a value written by the new code is read back by every reader of that secret.

## 3. Registry provisioning

- [x] 3.1 `OptRegistryCredentialsPage.cs:146`, `OptRegistrySqlServerPage.cs:144`, `OptRegistryUpdatesPage.cs:200` decrypt values an administrator provisions. Establish what an administrator is documented to put there before touching these.
- [x] 3.2 Most likely outcome: these stay decrypt-only on the legacy provider, since a documented provisioning format is an external contract even when the file is ours. If so, say that in the code — an untouched legacy call site with no explanation reads as an oversight.
- [x] 3.3 Tests: provisioning through the registry still works exactly as documented.

## 4. Close the door

- [x] 4.1 Confirm no settings write path constructs `LegacyRijndaelCryptographyProvider` directly. A test asserting this is worth more than the review that finds it once.
- [x] 4.2 Leave the SQL backend's use alone — `encrypt-sql-backend-with-aead` owns it, and touching both from two changes is how one of them gets half done.
- [x] 4.3 Assert in a test that no settings secret is protected by `ProtectedData`. `replace-default-connection-file-key` introduces that protector for the connection file and extending it here would break the portable edition on the second machine it reached — silently, and in code paths with nowhere to prompt.

## 5. Verification

- [x] 5.1 Full build; zero new analyzer warnings.
- [x] 5.2 Full test suite; zero failures, no `[Ignore]`.
- [x] 5.3 `openspec validate retire-legacy-rijndael-for-settings --strict`.
- [x] 5.4 Manual: with settings written by the current release, confirm each of the six secrets still works — default credentials on an RDP connection, a SQL Server connection, an update check through an authenticated proxy, an SSH connection, an external tool using the password token. — Passed on `DefaultPassword`, which is the setting behind three of the six: RDP default credentials, the SSH resolver and the external-tool password token all read it. A legacy unmarked value was written into the store and read back correctly through the fallback. The SQL and proxy secrets were **not** exercised individually; they go through the identical `SettingsSecretProtector.Unprotect` call with no branch between them, so what is untested is the wiring of two call sites rather than the migration itself.

  That wiring is now guarded rather than argued: `SettingsSecretCallSiteTests.EverySecretSettingIsReadThroughTheProtector` fails if any statement reads `Default.DefaultPassword`, `Default.SQLPass` or `Default.UpdateProxyAuthPass` without going through the protector — the failure that needs neither a SQL Server nor a proxy to matter, because it hands an `aead1:` string to a server as the password. The exceptions are listed with reasons: registry provisioning writes the stored form, and `DatabaseProfile` moves it between the settings and a profile without ever reading it. Confirmed non-vacuous by making `DatabaseConnectorFactory` read the setting raw and watching it fail.
- [x] 5.5 Manual: re-save each secret, confirm the stored value changes shape, confirm it still works. — 44 characters unmarked became 90 characters behind `aead1:`, and that value decrypts back to the original plaintext. The round trip is what makes this evidence rather than observation: a faulty legacy read would have re-encrypted whatever it got, so matching plaintext proves the read in 5.4 was correct and not merely non-empty.

  **A secret changed in Options is not on disk until the application exits.** `frmOptions` saves the page into the settings object; `SettingsSaver` writes it at shutdown. Neither OK nor Apply reaches the file, so anything that kills the process in between loses the change with nothing said.
- [x] 5.6 Manual: corrupt one migrated value in the settings file and confirm it reports a failure rather than returning a wrong password to a connection. — The security half held first time: one flipped character inside the payload was refused by the GCM tag, and nothing was returned. **The reporting half failed twice**, and both were fixed here.

  First, `frmOptions.InitOptionsPage` called `page.LoadSettings()` unguarded and added the page to the list only afterwards, so the exception aborted the loop that builds the dialog. One unreadable secret produced an empty Options window with no message — and Options is the only place the bad value can be corrected, so the failure locked the user out of its own remedy. That path was unreachable before this change, because the legacy provider returned plausible bytes instead of throwing.

  Second, with the page restored, the message still went nowhere a user would look: `AddExceptionStackTrace` defaults to `logOnly: true`. Now reported to the message collector as well, saying the value was left blank and that re-entering it will replace it.

## Findings from implementation

- **24 call sites, not 11.** The proposal counted the ones the audit listed. `SqlServerPage` has
  four, `UpdatesPage` two, and `DatabaseProfile` moves stored ciphertext in and out of `SQLPass`
  verbatim — so profiles stay consistent for free, but only because both ends go through the
  protector.
- **`SecurityPage.cs:172` is the provisioning contract, not a stored secret.** It is a generator: an
  administrator encrypts a value with it and puts the result in the registry, and the three
  `OptRegistry*` pages decrypt that format. Those four have to move together or not at all, and
  moving them would break every value already deployed. They stay legacy, now with the reason in
  the code rather than only here.
- **The registry path stores ciphertext, not plaintext.** `OptRegistryCredentialsPage.ApplyDefaultPassword`
  decrypts only to test whether the value is decryptable, discards the result, and stores the value
  still encrypted. So provisioned values arrive unmarked and readers handle them by the same rule
  that handles anything written before this change.
- **Key derivation cost measured before wiring the hot paths.** 244 ms for the first read, 0 ms for
  twenty more through the shared instance. An unset secret costs nothing at all, which is the common
  case. `RepeatedReadsReuseTheDerivedKey` asserts the sharing rather than trusting it.
- **`LegacyInsecureCryptoProviderFactory` has no callers.** Left alone; it belongs to the connection
  file work, not here.
