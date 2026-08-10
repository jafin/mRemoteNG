## ADDED Requirements

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

#### Scenario: A database with no stored sentinel

- **WHEN** the database holds no sentinel value
- **THEN** the database is treated as not yet initialised
- **AND** no key is returned without the sentinel having been verified

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
