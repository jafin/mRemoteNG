## ADDED Requirements

### Requirement: A connection file with no master password is protected with a machine-bound key

The system SHALL protect a connection file that has no master password with a randomly generated
per-file key, wrapped by the operating system's per-user data protection, and SHALL NOT derive that
file's key from a value present in the application's source.

A key that is a public constant provides no confidentiality. A file copied to a backup, a sync
folder, a roaming profile or a shared drive is readable by anyone who obtains it, which is the whole
of CVE-2023-30367 and the default state for every user who never opens the security options.

#### Scenario: Saving without a master password

- **WHEN** a connection file is saved and no master password is set
- **THEN** a random per-file key protects its contents
- **AND** that key is stored wrapped by per-user data protection
- **AND** the legacy default key is not used

#### Scenario: The file is opened by the account that wrote it

- **WHEN** a machine-bound file is opened by the user account that saved it
- **THEN** the key is unwrapped and the connections decrypt
- **AND** the user is not prompted

#### Scenario: The file is opened by a different account

- **WHEN** a machine-bound file is opened by a different user account
- **THEN** no connection is decrypted
- **AND** the message explains that the file is bound to the account that created it

### Requirement: The file declares which protection it uses

The connection file SHALL declare whether it is protected by a master password, by a machine-bound
key, or by the legacy default key, and the reader SHALL refuse a declaration it does not recognise.

The reader decides how to obtain a key before it can decrypt anything, so the declaration has to be
readable without one. Treating an unrecognised declaration as the legacy default would mean a build
that predates a future protection attempts `mR3m` against a file that never used it and reports a
wrong password.

#### Scenario: Each protection is declared

- **WHEN** a connection file is written
- **THEN** it declares which of the three protections applies

#### Scenario: An unrecognised declaration

- **WHEN** a connection file declares a protection the build does not recognise
- **THEN** no connection is loaded
- **AND** the message says the file was written by a newer version

### Requirement: Files written with the legacy default key remain readable

The system SHALL decrypt connection files declaring the legacy default key, and SHALL NOT write a
file under that key on the installed edition.

Fifteen years of files were written this way. Making them unreadable to fix how they are written
would destroy exactly what the change exists to protect.

#### Scenario: Opening a legacy file

- **WHEN** a connection file declares the legacy default key
- **THEN** it decrypts with that key
- **AND** it opens with no prompt and no migration step

#### Scenario: Saving a legacy file on the installed edition

- **WHEN** such a file is saved by the installed edition
- **THEN** it is rewritten with a machine-bound key
- **AND** it no longer declares the legacy default key

### Requirement: Migration is announced before it happens and preserves an escape route

The system SHALL inform the user before first writing a machine-bound file, SHALL state that the file
will no longer be readable from another account or machine, SHALL offer an export to a
password-protected file, and SHALL proceed only on confirmation.

Binding a file to a Windows account is not reversible by the user and breaks a workflow people rely
on — copying `confCons.xml` to a rebuilt machine. Discovering that after a reinstall means the
connections are gone.

#### Scenario: The first save after upgrading

- **WHEN** a legacy file is saved for the first time by a build that supports machine-bound keys
- **THEN** the user is told the file will become account-bound
- **AND** an export to a password-protected file is offered
- **AND** the migration proceeds only on confirmation

#### Scenario: The user declines

- **WHEN** the user declines the migration
- **THEN** the file is saved under the legacy default key
- **AND** the user is asked again on a later save rather than the choice being remembered silently

### Requirement: The portable edition is never bound to a machine

The portable edition SHALL NOT protect a connection file with a machine-bound key, SHALL offer a
master password instead, and SHALL state plainly when a file is left under the legacy default key.

A key bound to one Windows account cannot be unwrapped on the next machine, which is the only thing a
portable build is for. Silently applying it would break the edition; silently keeping the legacy key
without saying so is the present state and is what makes users believe the file is encrypted.

#### Scenario: Saving from the portable edition without a master password

- **WHEN** the portable edition saves a file and no master password is set
- **THEN** no machine-bound key is used
- **AND** the user is offered a master password
- **AND** declining leaves the legacy default key in place with the weakness stated

#### Scenario: A portable file moves between machines

- **WHEN** a file written by the portable edition is opened on another machine
- **THEN** it opens under its master password, or under the legacy default key if none was set
