# sql-backend-encryption Specification

## Purpose
TBD - created by archiving change encrypt-sql-backend-with-aead. Update Purpose after archive.
## Requirements
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

#### Scenario: A database at the new version

- **WHEN** a database records the authenticated-encryption version
- **THEN** its secrets are read and written with authenticated encryption

#### Scenario: A database above any version this build knows

- **WHEN** a database records a version newer than this build supports
- **THEN** no provider is selected and the database is refused

Stated separately because "at or above" would have selected the authenticated-encryption provider for
a database written by a build that knows more than this one. Its rows may be protected in a way this
provider has no code for, and reading them with it yields plausible nonsense — which is the outcome
refusing a newer database exists to prevent.

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

The system SHALL upgrade a SQL database to authenticated encryption only when the user asks for it
from the SQL configuration, SHALL NOT prompt for it when a database is opened, SHALL state before
proceeding that older clients will no longer read the database, and SHALL re-encrypt every stored
secret and raise the version within a single transaction.

A SQL database is shared, so the decision belongs to whoever administers it rather than to whoever
opens the application first. Upgrading changes the format for a whole team, cannot be undone without
restoring a backup, and locks out every client that has not been upgraded — so offering it to an
arbitrary user is offering it to someone who may have no authority to accept. Upgrading as a side
effect of an ordinary save would be worse still, and splitting the re-encryption from the version
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

#### Scenario: Opening a legacy database does not offer the upgrade

- **WHEN** a database at the legacy version is opened by a build that supports authenticated
  encryption
- **THEN** the upgrade is not prompted
- **AND** the database opens and is usable

#### Scenario: Where the upgrade is reachable

- **WHEN** the user opens the SQL configuration
- **THEN** the upgrade is available there

#### Scenario: The user is warned first

- **WHEN** the upgrade is offered
- **THEN** the user is told that clients on older builds will stop reading the database
- **AND** upstream mRemoteNG is named among them
- **AND** the user is told the change affects colleagues who did not install this fork
- **AND** the upgrade proceeds only on confirmation

### Requirement: A database using authenticated encryption requires a master password

The system SHALL require a master password for a SQL database at the authenticated-encryption
version, and SHALL NOT fall back to the built-in default key at that version.

The default key is a constant published in the application's source. A shared database keyed with it
is readable by anyone holding read access to the connections table, which for a team database is
routinely more people than are trusted with the credentials it stores.

#### Scenario: Opening an upgraded database

- **WHEN** a database at the authenticated-encryption version is opened
- **THEN** the master password is required
- **AND** the built-in default key is not attempted

#### Scenario: Upgrading a database

- **WHEN** a legacy database is upgraded to authenticated encryption
- **THEN** a master password must be supplied
- **AND** the upgrade does not proceed without one

#### Scenario: No password supplied

- **WHEN** the master password is not supplied or is refused
- **THEN** no connection is loaded
- **AND** nothing is written to the database

### Requirement: Legacy databases continue to open under the default key

The system SHALL continue to decrypt SQL databases below the authenticated-encryption version using
the existing key resolution, including the built-in default.

Refusing them would destroy access to a team's connections in order to change how they are stored.
Writing is already refused at that version, which is the pressure to upgrade; reading is not where
that pressure belongs.

#### Scenario: Opening a legacy database

- **WHEN** a database below the authenticated-encryption version is opened
- **THEN** it is decrypted as it is today
- **AND** no master password is required if none was set

### Requirement: The authentication sentinel is verified by its contents

The system SHALL confirm that decrypting the SQL database's stored sentinel yields a recognised
sentinel value, and SHALL NOT treat a decryption that merely completed as proof of the key.

The legacy provider is AES-CBC with PKCS7 and no authentication tag, so a wrong key yields valid
padding for roughly one attempt in 256 and returns arbitrary bytes rather than failing. The XML path
already compares the plaintext; the SQL path decrypts the same sentinel and discards it, so a wrong
password can pass the check and expose the hostnames, usernames and ports, which are not encrypted.

#### Scenario: The correct password

- **WHEN** the supplied password decrypts the sentinel to a recognised value
- **THEN** authentication succeeds

#### Scenario: A wrong password that decrypts without error

- **WHEN** a wrong password decrypts the sentinel without throwing
- **AND** the result is not a recognised sentinel value
- **THEN** authentication fails
- **AND** the user is prompted again within the existing attempt limit

#### Scenario: A database with no stored sentinel, at the authenticated-encryption version

- **WHEN** the database records the authenticated-encryption version or later
- **AND** it holds no sentinel value
- **THEN** the database is treated as not yet initialised
- **AND** no key is returned without the sentinel having been verified

#### Scenario: A database with no stored sentinel, below that version

- **WHEN** the database records a version below the authenticated-encryption version
- **AND** it holds no sentinel value
- **THEN** the existing legacy key resolution is used unchanged

Scoped to the version, because an empty sentinel means two different things. At the
authenticated-encryption version it means the database was never initialised and there is nothing to
verify a password against. Below it, it means that particular database has no master password
configured — a legacy database with one stores a sentinel — and refusing those would lock out
installations that work today.

### Requirement: A SQL database is reachable from any machine and any edition

The system SHALL protect a SQL database with a key derived only from the master password, and SHALL
NOT bind it to a machine, a user account, or an edition.

The store is shared by definition, and it is reached from installed and portable builds alike. A
protector bound to per-user data protection cannot be unwrapped by a second client, and one bound to
a machine cannot be unwrapped by a portable build carried to another. A password is the only
protector that travels to everyone who needs it.

#### Scenario: A second client opens the database

- **WHEN** a client that did not create the database opens it with the master password
- **THEN** the connections decrypt

#### Scenario: The portable edition opens the database

- **WHEN** the portable edition opens the database with the master password
- **THEN** the connections decrypt
- **AND** nothing about the key depends on which machine it is running on

### Requirement: A database still on the default key is identified as unprotected

The system SHALL indicate, where the SQL connection is configured, that a database using the built-in
default key is not meaningfully encrypted.

The current interface shows a stored password field and reports no error, which reads as protection.
An administrator cannot choose to fix something they have not been told about, and this is the state
every SQL database is in unless someone set a password.

#### Scenario: A legacy database with no master password

- **WHEN** a SQL database using the built-in default key is in use
- **THEN** the configuration surface states that its stored secrets are not protected
- **AND** points to the upgrade

#### Scenario: A database with a master password

- **WHEN** a SQL database is protected by a master password
- **THEN** no such warning is shown

