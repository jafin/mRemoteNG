## ADDED Requirements

### Requirement: SQL connection secrets are encrypted with authenticated encryption

The system SHALL encrypt connection secrets written to a SQL database using AES-256-GCM with a key
derived by PBKDF2, and SHALL NOT write them with the legacy MD5-keyed provider.

An unsalted MD5 of the master password is recoverable at GPU speed, and AES-CBC without a tag lets
anyone with write access to the table alter a stored password undetectably. A team using the SQL
backend currently gets both, while the XML backend on the same build gets neither.

#### Scenario: Saving a connection to an upgraded database

- **WHEN** connections are saved to a database at the authenticated-encryption version
- **THEN** each secret column is written as AES-256-GCM ciphertext
- **AND** each record carries its own salt and nonce

#### Scenario: Tampered ciphertext is rejected

- **WHEN** a stored secret is altered in the database
- **THEN** decryption fails and is reported
- **AND** no value is returned for that secret

### Requirement: The provider follows the database version

The system SHALL choose the cryptography provider for a SQL database from its recorded version, and
SHALL apply one provider to the whole store rather than deciding per value.

Deciding per value would make a half-migrated table readable, which is the state that must be
impossible: it would mean an interrupted upgrade left an unknown subset of secrets weakly protected
with no way to identify them.

#### Scenario: A database below the new version

- **WHEN** a database records a version below the authenticated-encryption version
- **THEN** its secrets are read with the legacy provider

#### Scenario: A database at or above the new version

- **WHEN** a database records the authenticated-encryption version
- **THEN** its secrets are read and written with authenticated encryption

### Requirement: A database at the legacy version is not written

The system SHALL refuse to save connections to a database still at the legacy version, and SHALL
direct the user to the upgrade.

Continuing to write the legacy format would mean new secrets are still protected by an unsalted MD5,
and doing it silently would leave users believing the fix had reached them.

#### Scenario: Saving to a legacy database

- **WHEN** a save is attempted against a database at the legacy version
- **THEN** the save does not proceed
- **AND** the user is told the database must be upgraded and how

### Requirement: A database newer than the running build is refused

The system SHALL refuse to read a SQL database whose recorded version is newer than the build
understands, and SHALL report the version rather than attempting to decrypt.

Unauthenticated decryption of AES-CBC returns plausible bytes for input it was never given. An older
client reading an upgraded database would show empty or corrupt passwords for connections that
previously worked, which reads to a user as data loss rather than as a version mismatch.

#### Scenario: An older build opens an upgraded database

- **WHEN** a database records a version the build does not understand
- **THEN** no connection is loaded
- **AND** the message names the database version and the version the build supports

### Requirement: Upgrading a database is explicit and atomic

The system SHALL upgrade a SQL database to authenticated encryption only when the user asks, SHALL
state before proceeding that older clients will no longer read the database, and SHALL re-encrypt
every stored secret and raise the version within a single transaction.

A SQL database is shared. Upgrading as a side effect of an ordinary save would change the format for
a whole team because one person edited a connection. Splitting the re-encryption from the version
change would leave a store that is neither format if it were interrupted.

#### Scenario: Upgrading

- **WHEN** the user upgrades a legacy database and supplies the master password
- **THEN** every stored secret is re-encrypted with authenticated encryption
- **AND** the recorded version is raised
- **AND** both happen in one transaction

#### Scenario: The upgrade fails partway

- **WHEN** the upgrade fails before committing
- **THEN** the database is left entirely at the legacy version
- **AND** every secret remains readable by the current build

#### Scenario: The master password is wrong

- **WHEN** the supplied password does not authenticate against the stored verifier
- **THEN** the upgrade does not begin
- **AND** no row is modified

#### Scenario: The user is warned first

- **WHEN** the upgrade is offered
- **THEN** the user is told that clients on older builds will stop reading the database
- **AND** the upgrade proceeds only on confirmation
