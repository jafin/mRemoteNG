# Design

## Context

Three pieces decide how a SQL database is keyed:

| Piece | Where | Behaviour today |
|---|---|---|
| Sentinel write | `SqlDatabaseMetaDataRetriever.WriteDatabaseMetaData` | `ThisIsProtected` under the master password, or `ThisIsNotProtected` under `Runtime.EncryptionKey` |
| Key retrieval | `SqlConnectionsLoader.GetDecryptionKey` | Empty sentinel ⇒ `mR3m` unchecked; otherwise try `mR3m`, then prompt |
| Verification | `PasswordAuthenticator.Authenticate` | Success is "`Decrypt` did not throw" |

`Runtime.EncryptionKey` is `RootNodeInfo.PasswordString` (`Runtime.cs:66`), so the unprotected write
and the unprotected read agree on `mR3m` and a team database looks encrypted while being open to
anyone with read access to the table.

## Goals

- A SQL database protected by authenticated encryption is protected by a real key.
- No existing database becomes unreadable.
- An administrator understands the consequence before it is irreversible.

## Non-Goals

- Changing the cipher or KDF. `encrypt-sql-backend-with-aead` owns those and must land first.
- A key server, escrow, or integration with the credential vaults. Worth wanting; far larger.
- Fixing the XML default key. `replace-default-connection-file-key` owns that, and its answer —
  per-user DPAPI — is specifically the one that cannot work here.
- Per-user access control on a shared store. One master password for the database is what the current
  model supports; anything finer is a different product.

## Decisions

### Requiring the password rides on the version, not on a new setting

`encrypt-sql-backend-with-aead` already gates provider selection on `ConfVersion` and already makes
the upgrade an explicit, password-supplying action. Requiring a master password *at that version*
adds no new state, no new prompt and no new decision point. A separate "require password" toggle
would be a setting that can be turned off, on a store where turning it off is silently catastrophic.

The upgrade is also the only moment where every secret is being rewritten anyway, so it is the one
point where changing the key costs nothing extra.

### DPAPI is not an option, and the reason is worth writing down

`replace-default-connection-file-key` wraps a per-file key with `ProtectedData.Protect` at
`CurrentUser` scope. On a shared database that produces a key exactly one Windows account can unwrap,
which is not a shared database any more. `LocalMachine` scope would fail differently — every account
on every client machine could unwrap it, including services an attacker already controls, and the
blob would still have to be distributed to each client.

There is no per-user protector that survives being shared. That is why the answer here is a password
a human distributes, and why this could not be folded into the file-key proposal.

### Verify the sentinel, don't only decrypt it

`PasswordAuthenticator` is used by both backends and the XML caller compares the plaintext itself
(`XmlConnectionsDecryptor.cs:145`). Rather than change the shared authenticator's contract, give the
SQL caller the same comparison: decrypt, then require the result to be one of the two known
sentinels.

This is independent of everything else here and of `encrypt-sql-backend-with-aead`. It should ship on
its own, first — a one-in-256 authentication pass is not worth leaving in place while a multi-release
crypto migration proceeds.

Once the store is AEAD, a wrong key fails the tag and cannot reach the comparison at all, so the
check becomes belt-and-braces. It is still correct to have: the check is what makes the *legacy*
path safe in the interim, which is the period that matters.

### Legacy databases keep reading under the default key

Refusing them would delete a team's connections to fix how they are stored. They keep working, and
`encrypt-sql-backend-with-aead` already refuses to *write* them, which is the pressure to upgrade.

## Risks

| Risk | Mitigation |
|---|---|
| Upgrade locks out colleagues who were not told the password | The upgrade warning states it; documentation states it; the password is set by a human before the upgrade runs |
| Forgotten master password loses the database | Stated at the point the password is set, not in release notes |
| Administrator picks a weak password | The KDF from `encrypt-sql-backend-with-aead` is what absorbs this; there is no substitute for the password being good |
| Sentinel change breaks the XML path | The comparison is added at the SQL caller; the shared authenticator's behaviour is unchanged |

## Open Questions

Both are now answered. Kept rather than deleted, because what was rejected is part of the record.

- ~~Should the pre-upgrade check establish how many distinct clients have recently written to the
  database, so the warning can say "this database has been used by N clients"?~~ **No — the schema
  cannot support it.** `tblUpdate` holds exactly one row: every save deletes all rows and inserts a
  single `LastUpdate` stamp. There is no client identity and no history, so the most it could say is
  when the database was last written, by nobody in particular. A trustworthy count needs a new table
  and a write on every save, which is a schema change this proposal has no business making — and it
  would improve a warning that is already explicit that the decision reaches people who are not in
  the room.
- ~~Is there a case for an explicit, acknowledged "stay on the legacy version" state?~~ **No.** Doing
  nothing already has that effect, and naming it would legitimise leaving passwords under `mR3m`
  while adding a setting whose only function is to record a decision not to act. The options page
  now says what that state costs, which is the honest version of the same information.
