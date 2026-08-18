## ADDED Requirements

### Requirement: A local copy of a store carries the store's protection, never the published key

The system SHALL protect a local cached copy of a connection store at least as well as the store it
copies, and SHALL NOT write such a copy under `ConnectionFileDefaults.LegacyEncryptionKey`.

The SQL connections cache is written on every successful load, through the ordinary connection-file
saver with a default save filter, so it holds every password in the store. Its key comes from the
root node's `PasswordString`, which is marked protected only when it differs from the default — so a
database with no master password produces a local file encrypted under a constant published in
mRemoteNG's own source code. Upgrading that database to authenticated encryption does nothing for
the copy in `%APPDATA%`, and the copy is on a workstation rather than a server.

#### Scenario: Caching a store with no master password

- **WHEN** a store with no master password is cached locally
- **THEN** the copy is not written under the legacy published key

#### Scenario: Caching a store with a master password

- **WHEN** a store protected by a master password is cached locally
- **THEN** the copy is protected at least as well

#### Scenario: A store that cannot be cached safely

- **WHEN** a local copy cannot be given protection at least equal to the store's
- **THEN** no copy is written
- **AND** the user is told that offline access is unavailable for this store
