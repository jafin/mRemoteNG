# Tasks

Section 1 is the defect. Section 2 is the exposure found beside it and could be split into its own
change if it slows section 1 down — section 1 is the one holding back
`encrypt-sql-backend-with-aead` §1 from reaching anybody.

## 1. Refuse the write

- [x] 1.1 Carry "this model came from a fallback copy" on the loaded model, not in `Properties.OptionsDBsPage.Default.SQLReadOnly`. That setting is a user preference; borrowing it means writing to the user's settings to record a transient failure and restoring it later from code that runs when things are already going wrong — and a missed restore leaves the database silently read-only for ever, against a checkbox they never ticked.
- [x] 1.2 Refuse in `SaveConnections` when the flag is set, reporting through the existing not-performed path so `connection-save-durability`'s "visible without the log" requirement covers it.
- [x] 1.3 Clear the flag only when a load from the real source succeeds.
- [x] 1.4 Confirm the debounced/auto-save path refuses too, not only the explicit one. The dangerous save here is the one nobody asked for. — The check sits in `SaveConnectionsUnderLock`, which every path funnels through: explicit, debounced and batched. Putting it in the public entry point instead would have left the batched path to refuse only when the batch drained, and `forceSave` bypassing it entirely — that flag exists to bypass the "nothing is loaded" guard, and the reason for refusing here is not an odd application state, it is that writing would destroy other people's work. There is a test for exactly that.
- [x] 1.5 Tests: a save after a fallback load writes nothing and reports; a save after a normal load is unaffected; a successful reload restores saving; the debounce path refuses. The first is the regression test — today it silently overwrites.

## 2. Stop caching secrets under the published key

- [x] 2.1 Decide between hardening the cache file and not caching an unprotected store — design.md recommends hardening, and says why deleting the feature is the worse trade. — **Hardened, and not through the connection file's key slots.** That format refuses a machine protector with no recovery protector by design (`ConnectionFileKeyProtection.Read`), because a connection file that opens on exactly one machine is one somebody eventually loses. Prompting for a recovery password to read a cache is absurd, and weakening that invariant to avoid the prompt would be worse. So the cache carries a random 32-byte key of its own, DPAPI-wrapped for the account that wrote it, in a sidecar beside the file.
- [x] 2.2 Implement it in `TrySaveSqlConnectionsCache`, which currently hands `XmlConnectionsSaver` a root node whose `PasswordString` is the database's decryption key — correct for a database with a master password, and the published `mR3m` for one without.
- [x] 2.3 Delete any existing cache written under the legacy key on upgrade rather than leaving it behind. A fix that leaves the exposed file on disk has not fixed anything for the people who already have one. — `DiscardIfUnprotected`, called from both the write path and the fallback path. A cache with no sidecar key is one written before this existed; it is deleted and the removal is logged, naming what it held.
- [x] 2.4 Tests: the cache of an unprotected store is not readable with the legacy key; the cache of a protected store is readable with its master password; a store that cannot be cached safely produces no cache.

## 3. Say what is true

- [x] 3.1 Replace "Loading from local cache in read-only mode" — it names the one restriction that would stop a user editing, and does not enforce it.
- [x] 3.2 Include the copy's age. Four minutes and four months call for different decisions and only the application knows which this is.
- [x] 3.3 Say plainly how to recover: reconnect and reload. The current text leaves the user to discover the state by editing and finding out.
- [x] 3.4 Document it in `docs-website/docs/sql-configuration.md` — offline behaviour is user-visible and currently undocumented.

## 4. Verification

Note on 4.4 and 4.5: both are manual and **not done**. Every automated test here drives a substitute
for the thing that goes wrong — a model with a flag set, rather than a database that actually went
away — so the end-to-end path is unproven.

- [x] 4.1 Full build; zero new analyzer warnings.
- [x] 4.2 Full test suite; zero failures.
- [x] 4.3 `openspec validate refuse-writes-from-a-cached-fallback --strict`.
- [ ] 4.4 Manual, against the container: load a database, stop the container, restart mRemoteNG so the fallback fires, edit a connection, and confirm nothing reaches the database when it comes back. **This is the whole defect** — do it by hand once, because it is the case where the automated tests are testing a substitute for the thing that goes wrong.
- [ ] 4.5 Manual: confirm the same for a database refused by version, which is the path `encrypt-sql-backend-with-aead` §1 depends on and the reason this change exists.
