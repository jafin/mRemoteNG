## ADDED Requirements

### Requirement: A hardened connection file is protected by a random per-file key

The system SHALL protect a connection file at the hardened format level with a randomly generated
per-file key, and SHALL NOT derive such a file's key from a value present in the application's source.

A key that is a public constant provides no confidentiality. A file copied to a backup, a sync
folder, a roaming profile or a shared drive is readable by anyone who obtains it, which is the whole
of CVE-2023-30367 and the default state for every user who never opens the security options.

It applies at the hardened level only because upstream mRemoteNG reads the same file from the same
path and does not recognise the sentinel a per-file key requires.

#### Scenario: Saving a hardened file

- **WHEN** a connection file at the hardened level is saved
- **THEN** its contents are encrypted with a random per-file key
- **AND** the legacy default key is not used

#### Scenario: Saving a classic file

- **WHEN** a connection file at the classic level is saved
- **THEN** it keeps the key source upstream mRemoteNG understands
- **AND** no per-file key is written

### Requirement: The per-file key has both a machine protector and a recovery protector

The system SHALL wrap the per-file key with the operating system's per-user data protection and with
a key derived from a user-supplied recovery password, SHALL store both wrapped copies in the file,
and SHALL open the file using whichever protector is available.

A single machine-bound protector makes the file unreadable after a profile rebuild, and because
backups are byte-identical copies of the encrypted file, it makes every backup unreadable with it. A
recovery protector travels with every copy, so confidentiality improves without recoverability
getting worse.

#### Scenario: Opening on the machine that wrote the file

- **WHEN** a protected file is opened by the account that saved it
- **THEN** the key is unwrapped using per-user data protection
- **AND** the user is not prompted

#### Scenario: Opening where per-user data protection cannot help

- **WHEN** a protected file is opened by a different account, on a different machine, or after a
  profile rebuild
- **THEN** the recovery password is requested
- **AND** supplying it decrypts the file

#### Scenario: Neither protector available

- **WHEN** per-user data protection cannot unwrap the key and the recovery password is not supplied
- **THEN** no connection is decrypted
- **AND** the message distinguishes this from a corrupt file

### Requirement: A recovery password is required before a per-file key is written

The system SHALL obtain a recovery password before writing a per-file key for the first time, and
SHALL NOT write a file whose only protector is machine-bound.

A file with one machine-bound protector is lost when the profile is rebuilt, and its entire backup
history is lost with it. The user has no signal that this is so until they need a backup, at which
point every copy they hold is equally unopenable.

#### Scenario: Raising a store to the hardened level

- **WHEN** the user raises a store to the hardened level
- **THEN** a recovery password is requested before the file is written
- **AND** the file is written only once one is supplied

#### Scenario: The user declines to set a recovery password

- **WHEN** the user declines to supply a recovery password
- **THEN** the store stays at the classic level with the protector it already had
- **AND** a file under a user's master password keeps that password
- **AND** a file under the legacy default key keeps that key
- **AND** the level is not raised

A classic file is not always under the legacy default key: it carries a master password whenever the
user set one. Describing every declined migration as remaining under the default key would either
misdescribe those files or, if implemented literally, take their master password off them — the
opposite of what declining a security upgrade should do.

### Requirement: Backups of a protected file remain restorable

The system SHALL ensure that a copy of a protected connection file can be decrypted with the recovery
password on any machine.

`FileBackupCreator` copies the encrypted file without re-encrypting it, so a backup carries exactly
the protectors the original had. Backups exist to survive the loss of the machine, and users direct
them at network shares and synced folders for that reason.

#### Scenario: Restoring a backup on another machine

- **WHEN** a backup of a protected file is opened on a machine that did not write it
- **THEN** the recovery password decrypts it
- **AND** the connections are recovered

#### Scenario: Restoring a backup on the original machine

- **WHEN** a backup of a protected file is opened by the account that wrote it
- **THEN** it opens without a prompt

### Requirement: Automatic recovery distinguishes an unreadable key from a corrupt file

The system SHALL treat a failure to unwrap the per-file key as distinct from a failure to parse the
file, and SHALL NOT iterate over backup files when the key could not be unwrapped.

Automatic recovery walks every backup on a load failure and reports each one it cannot read. A key
that cannot be unwrapped fails identically for every backup, so the user is shown a series of
warnings implying their backups are corrupt when the files are intact and only the protector is out
of reach. Recovery also overwrites the live file with the first backup that parses, so reaching that
path for the wrong reason is destructive.

#### Scenario: The key cannot be unwrapped

- **WHEN** loading fails because the per-file key could not be unwrapped
- **THEN** the backup set is not searched
- **AND** the recovery password is requested
- **AND** the live file is not overwritten

#### Scenario: The file is genuinely corrupt

- **WHEN** loading fails because the file cannot be parsed
- **THEN** the existing backup recovery proceeds as it does today

### Requirement: The file declares which protection it uses

The connection file SHALL declare whether it is protected by a master password, by a per-file key, or
by the legacy default key, and the reader SHALL refuse a declaration it does not recognise.

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

The system SHALL decrypt connection files declaring the legacy default key, and SHALL continue to
write under that key at the classic format level.

Fifteen years of files were written this way. Making them unreadable to fix how they are written
would destroy exactly what the change exists to protect, and the classic level is also what keeps a
store readable by upstream mRemoteNG.

#### Scenario: Opening a legacy file

- **WHEN** a connection file declares the legacy default key
- **THEN** it decrypts with that key
- **AND** it opens with no prompt and no migration step

#### Scenario: Saving a legacy file that was not upgraded

- **WHEN** a legacy file is saved and its level was never raised
- **THEN** it is written under the legacy default key
- **AND** upstream mRemoteNG can still open it

### Requirement: The portable edition writes no machine-bound protector

The portable edition SHALL protect a connection file with the recovery password alone, SHALL NOT
write a per-user data protection protector, and SHALL state plainly when a file is left under the
legacy default key.

A protector bound to one Windows account cannot be unwrapped on the next machine, which is the only
thing a portable build is for. Writing one would produce a file the edition cannot open on the
machine it is carried to.

#### Scenario: Saving from the portable edition

- **WHEN** the portable edition saves a protected file
- **THEN** only the recovery-password protector is written

#### Scenario: A portable file moves between machines

- **WHEN** a file written by the portable edition is opened on another machine
- **THEN** the recovery password decrypts it

#### Scenario: The portable edition declines protection

- **WHEN** the portable edition saves and no recovery password is set
- **THEN** the legacy default key is used
- **AND** the user is told the file is protected by a key published in the application's source

### Requirement: Files are interchangeable between editions

A connection file written by either edition SHALL be readable by the other, given its recovery
password.

Users move a file between an installed machine and a USB stick, and a format split between the two
editions would turn an ordinary copy into data loss. Both editions write the same format; only the
presence of the machine-bound protector differs.

#### Scenario: An installed file opened by the portable edition

- **WHEN** a file written by the installed edition is opened by the portable edition
- **THEN** the recovery password decrypts it

#### Scenario: A portable file opened by the installed edition

- **WHEN** a file written by the portable edition is opened by the installed edition
- **THEN** the recovery password decrypts it
- **AND** a machine-bound protector may be added when it is next saved
