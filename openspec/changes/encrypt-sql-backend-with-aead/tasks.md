# Tasks

**This proposal is split across releases.** Section 1 ships a release ahead of the rest; sections 2
onward wait for `add-storage-format-opt-in`. See
[SECURITY-AUDIT-3416.md](../SECURITY-AUDIT-3416.md) for the full order.

## 1. Version refusal — ships first

- [x] 1.1 In `SqlDatabaseVersionVerifier`, treat a database version newer than the build as a refusal that names both versions, instead of proceeding to read rows. — Two defects, not one. The verifier did already return false, but reported it as a generic incompatibility warning; it now reports the newer case at error severity naming the database version, the product and the highest supported version (`ErrorDatabaseVersionNewerThanClient`). **`SqlConnectionsLoader` was discarding the result entirely** and reading rows regardless, so the check existed and changed nothing.
- [x] 1.2 Tests: a newer version loads nothing and reports both numbers; the current version still loads; an older version still loads. — `SqlDatabaseVersionVerifierTests` plus two `SqlConnectionsLoaderIntegrationTests` cases, one asserting the rows are never read.
- [x] 1.3 Release this ahead of the rest. Until every client refuses a newer database, upgrading one produces silent garbage on the others rather than a message — see design.md, "Refuse a database newer than the build".

**Scope note.** `IsNewerThanSupported` was added to `ISqlDatabaseVersionVerifier` rather than making
the loader honour `VerifyDatabaseVersion` outright. The two failure modes need opposite handling: a
database too old to upgrade also returns false, and refusing those would lock out installations that
work today. Only the newer case is refused.

## 2. Provider selection

- [x] 2.1 Add a version constant for authenticated encryption and a helper that returns the provider for a given `ConfVersion`, mirroring `CryptoProviderFactoryFromXml`'s role on the XML side. — `CryptoProviderFactoryFromSqlVersion`, with `AuthenticatedEncryptionVersion` at 3.6. A null version reads as legacy, and the asymmetry is the reason: reading legacy ciphertext with the AEAD provider fails cleanly because GCM authenticates, while the reverse does not fail at all — AES-CBC has no tag, so it yields plausible nonsense and the user sees connections with empty passwords, which reads as data loss rather than a version problem.
- [x] 2.2 Replace the hardcoded `new LegacyRijndaelCryptographyProvider()` in `ConnectionsService.cs:309` with the helper, fed by the metadata the loader already retrieves. — **The loader could not be given a provider at all.** It learns the version inside `Load()`, after the metadata is read, so the constructor now takes the *rule* — `Func<Version?, ICryptographyProvider>` — rather than a provider chosen before anything is known. One provider then serves both the sentinel and the rows, because they are encrypted together and a mismatch would authenticate a password that decrypts nothing.
- [x] 2.3 Replace the hardcoded provider in `SqlConnectionsSaver.cs:153` the same way. — The saver already retrieves the metadata, so the version was to hand.
- [x] 2.4 Check `SqlDatabaseMetaDataRetriever.cs:96`, which builds its own legacy provider, and route it through the helper or document why its use is version-independent. — Routed, and it is the **most** important field to route, not an afterthought. `Protected` is a fixed, published plaintext encrypted under the master password: at the legacy provider's unsalted MD5 derivation it is an ideal offline cracking oracle, testable at GPU speed by anyone with read access to `tblRoot`. Leaving it legacy while moving the rows to AEAD would preserve the cheapest attack on the whole database, and would also break the load, which reads both with one provider.
- [x] 2.5 Tests: the helper returns legacy below the version and AEAD at or above it; the loader and saver agree for a given version. — Ten cases in `CryptoProviderFactoryFromSqlVersionTests`, including that each call gets its own provider instance (the AEAD provider caches derived keys in fields, so sharing one across the saver and a batch decrypt would give intermittent wrong answers rather than a clean failure) and that the schema version and the AEAD version never coincide, which is what stops a newly created database silently choosing the format upstream mRemoteNG cannot read.

**Two defects found while wiring this, both outside what §2 asked for.**

`WriteDatabaseMetaData` wrote `ConnectionsFileInfo.ConnectionFileVersion` — the XML file-format
constant, **3.2** — into `tblRoot.ConfVersion` on every save. So a 3.5 database was stamped back to
3.2 by each save and the next load re-ran the 3.2→3.5 schema upgraders to put it back. Survivable
churn while nothing depended on the number; fatal once the number decides how secrets are encrypted,
because an upgraded database would be marked 3.2 by the first ordinary save while its rows were
written as AEAD — the half-migrated state design.md exists to make impossible, reached without anyone
doing anything wrong. It now preserves the database's own version.

`SqlDatabaseVersionVerifier` accepted exactly one version. Two are readable now — 3.5 and 3.6 share a
schema and differ only in encryption — so the check is a range. An equality against the new highest
version would have reported every database in the field as unsupported.

## 3. Refuse to write a legacy database

- [x] 3.1 ~~Make `SqlConnectionsSaver` refuse when the database is below the new version~~ — **task rewritten after implementing it.** The saver now warns once per database and saves anyway. Refusing was a bigger hammer than this change's own reasoning called for: design.md establishes that the *upgrade* must not be prompted, because it decides for a whole team and is irreversible — which says nothing about whether a legacy database should keep accepting writes. Refusing improves nothing today (the weak encryption is the state these databases are already in), stops work until an administrator acts, and because saves are automatic and debounced it lands on an ordinary rename. It also contradicted this fork's own precedent, where a classic connection file stays fully writable and hardening is offered rather than imposed. `WarnOnceIfTheDatabaseStillStoresSecretsWeakly`, keyed on server and database name so two stores on one server are warned about separately.
- [x] 3.2 Confirm the refusal surfaces where a user will see it rather than only in the log — a save that silently does nothing is worse than the weak encryption it avoids. — Moot in its original form, since nothing is refused, but the concern behind it decided the shape of the warning. It goes to the message channel and **never to a modal**: a save can run on the debounce timer rather than on the user's action, so a dialog could appear over unrelated work or off the UI thread. The text says the change *was* saved — it must not read as a failure — names **Tools → Options → SQL Server**, and states what upgrading costs: older builds and other mRemoteNG installations stop being able to open the database. That last part is the piece nobody can find out for themselves before acting.
- [x] 3.3 Tests: a save against a legacy database does not write and reports; a save against an upgraded database writes AEAD ciphertext. — **Restated by the change above, then done against a real SQL Server.** The first half is now "a legacy database is still written, and its secrets stay legacy" — refusing improved nothing and stopped people working. Five tests in `SqlSaverEncryptionIntegrationTests`, which are the only ones in this change that can answer what is actually *in* the column afterwards: a saver and a loader that disagree produce a database nothing reads back, and testing either end alone would never show it. Also pinned: the two formats are not interchangeable, so "it decrypts with the legacy provider" asserts something; the warning is raised once and not on the second and third save; and an upgraded database is not warned about at all.

  Two things had to be got right to drive the saver, both recorded in the fixture because neither failure resembles its cause. `SqlConnectionsSaver` builds its own connector from `DatabaseConnectorFromSettings()`, so the tests set the application's SQL settings and put them back — a seam worth adding if that file grows. The settings password must be stored **protected**: `SettingsSecretProtector`'s no-marker fallback legacy-*decrypts* rather than passing a value through, so a plain password fails inside the save as "not a valid Base-64 string". And `SQLAuthType` must be set explicitly, or the factory discards the credentials for integrated security and builds a DataSource with the colon and port still in it, which SqlClient answers thirty seconds later with "the server was not found" — about a container that is running perfectly.

**The cost of this decision, recorded so it is not lost:** a team that never opens the SQL options
page keeps the weak format indefinitely. Refusing writes would have forced the issue. What replaces
that pressure is the options page's status line, which reaches the person who can actually decide —
and the warning, which at least means nobody can say they were not told.

**§3 also invalidates a decision recorded in §2, and the note there has been corrected rather than
left to be discovered.** §2 said a new database is created at the schema version, reasoning from the
connection file: write the older format so upstream mRemoteNG can still read it. Nothing reads a
database this build has only just created, so there is nobody to stay compatible with and no reason
to start it weak; new databases are created at the authenticated version. An *existing* database is
still never upgraded except deliberately, which is the part of the original reasoning that survives.

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
