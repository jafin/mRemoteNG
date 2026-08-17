# Tasks

Section 1 is the defect. Section 2 is the exposure found beside it and could be split into its own
change if it slows section 1 down — section 1 is the one holding back
`encrypt-sql-backend-with-aead` §1 from reaching anybody.

## 1. Refuse the write

- [ ] 1.1 Carry "this model came from a fallback copy" on the loaded model, not in `Properties.OptionsDBsPage.Default.SQLReadOnly`. That setting is a user preference; borrowing it means writing to the user's settings to record a transient failure and restoring it later from code that runs when things are already going wrong — and a missed restore leaves the database silently read-only for ever, against a checkbox they never ticked.
- [ ] 1.2 Refuse in `SaveConnections` when the flag is set, reporting through the existing not-performed path so `connection-save-durability`'s "visible without the log" requirement covers it.
- [ ] 1.3 Clear the flag only when a load from the real source succeeds.
- [ ] 1.4 Confirm the debounced/auto-save path refuses too, not only the explicit one. The dangerous save here is the one nobody asked for.
- [ ] 1.5 Tests: a save after a fallback load writes nothing and reports; a save after a normal load is unaffected; a successful reload restores saving; the debounce path refuses. The first is the regression test — today it silently overwrites.

## 2. Stop caching secrets under the published key

- [ ] 2.1 Decide between hardening the cache file and not caching an unprotected store — design.md recommends hardening, and says why deleting the feature is the worse trade.
- [ ] 2.2 Implement it in `TrySaveSqlConnectionsCache`, which currently hands `XmlConnectionsSaver` a root node whose `PasswordString` is the database's decryption key — correct for a database with a master password, and the published `mR3m` for one without.
- [ ] 2.3 Delete any existing cache written under the legacy key on upgrade rather than leaving it behind. A fix that leaves the exposed file on disk has not fixed anything for the people who already have one.
- [ ] 2.4 Tests: the cache of an unprotected store is not readable with the legacy key; the cache of a protected store is readable with its master password; a store that cannot be cached safely produces no cache.

## 3. Say what is true

- [ ] 3.1 Replace "Loading from local cache in read-only mode" — it names the one restriction that would stop a user editing, and does not enforce it.
- [ ] 3.2 Include the copy's age. Four minutes and four months call for different decisions and only the application knows which this is.
- [ ] 3.3 Say plainly how to recover: reconnect and reload. The current text leaves the user to discover the state by editing and finding out.
- [ ] 3.4 Document it in `docs-website/docs/sql-configuration.md` — offline behaviour is user-visible and currently undocumented.

## 4. Verification

- [ ] 4.1 Full build; zero new analyzer warnings.
- [ ] 4.2 Full test suite; zero failures.
- [ ] 4.3 `openspec validate refuse-writes-from-a-cached-fallback --strict`.
- [ ] 4.4 Manual, against the container: load a database, stop the container, restart mRemoteNG so the fallback fires, edit a connection, and confirm nothing reaches the database when it comes back. **This is the whole defect** — do it by hand once, because it is the case where the automated tests are testing a substitute for the thing that goes wrong.
- [ ] 4.5 Manual: confirm the same for a database refused by version, which is the path `encrypt-sql-backend-with-aead` §1 depends on and the reason this change exists.
