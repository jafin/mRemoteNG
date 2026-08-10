# Design

## Context

The SQL backend stores one row per connection. Secret columns hold base64 ciphertext produced by
whichever `ICryptographyProvider` the saver was constructed with:

| Path | Where the provider is chosen |
|---|---|
| Save | `SqlConnectionsSaver.UpdateConnectionsTable` — `SqlConnectionsSaver.cs:153` |
| Load | `ConnectionsService` — `ConnectionsService.cs:309`, injected into `SqlConnectionsLoader` |

Both hardcode `LegacyRijndaelCryptographyProvider`. The XML path does not: it asks
`CryptoProviderFactoryFromXml` to pick from what the file records. The SQL path has no equivalent
because nothing in the schema described the encryption.

It does, however, already carry a version: `SqlConnectionListMetaData.ConfVersion`, retrieved by
`SqlDatabaseMetaDataRetriever` and checked by `SqlDatabaseVersionVerifier` before any row is read.

## Goals

- Connection secrets in SQL get the same protection as in XML.
- A database can be upgraded without a client silently producing garbage.
- A client that has not been upgraded keeps working until the database moves.

## Non-Goals

- Encrypting the whole row or the schema. Only the columns that hold secrets change.
- Changing how the master password is obtained or authenticated. `PasswordAuthenticator` and the
  `Protected` metadata field keep working as they do; only the provider they run through changes.
  (The `mR3m` default that `SqlConnectionsLoader.cs:86,89` falls back to is the subject of
  `replace-default-connection-file-key`, not this change.)
- A downgrade path. Once a database is at the new version it stays there; reverting means restoring
  a backup, which is what the pre-upgrade warning has to say.

## Decisions

### Gate on `ConfVersion`, not on the ciphertext

Sniffing would be possible — GCM output carries a salt and nonce prefix the legacy format does not —
but it decides per value. A half-migrated table would then be readable, and half-migrated is the
state we most want to be impossible: it means an interrupted upgrade left some rows recoverable at
GPU speed and no way to tell which. A version gate makes the store atomically one thing or the other,
and the re-encryption runs in the transaction that raises the version.

### Refuse a database newer than the build

`SqlDatabaseVersionVerifier` already compares versions. It must treat "newer than I understand" as a
refusal with a message naming the version, not as something to attempt. Without this, an un-upgraded
client reads AEAD ciphertext with the legacy provider, gets plausible-looking garbage out of
AES-CBC's unauthenticated decrypt, and shows a user empty passwords for connections that used to
work — the worst available outcome, because it looks like data loss rather than a version mismatch.

This is the one part of the change that has to ship **before** anyone upgrades a database. It belongs
in a release that precedes the one offering the upgrade, or the protection is not there when it is
first needed.

### Upgrade is an explicit action

Not the first save. A shared database changing format because one user happened to edit a connection
is how a team loses an afternoon. The upgrade is a deliberate command that states what it will do,
warns that older clients will stop reading the database, and requires the master password — which it
needs anyway, to re-encrypt.

### Re-encrypt in one transaction

Read every secret with the legacy provider, write it back with AEAD, raise `ConfVersion`, commit. The
saver already wraps its work in a `DbTransaction` (`SqlConnectionsSaver.cs:99`), so the machinery
exists. A failure rolls back to a database that is entirely legacy and entirely readable.

## Risks

| Risk | Mitigation |
|---|---|
| Older client reads a new database | Version refusal, shipped a release earlier |
| Upgrade interrupted | Single transaction; rollback leaves the old format intact |
| Master password wrong at upgrade time | Authenticate against `Protected` before touching a row |
| Users upgrade without warning others | The command states the consequence and requires confirmation |

## Open Questions

- Does the upgrade belong in the SQL options page, or as a first-run prompt when a legacy database is
  opened by a build that supports AEAD? The prompt reaches people who would never look in options;
  it also fires on every client until someone accepts, which is noise. Leaning towards the options
  page with a status line elsewhere, but this is worth deciding before task 4.
