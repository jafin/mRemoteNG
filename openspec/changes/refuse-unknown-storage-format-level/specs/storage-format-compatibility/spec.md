## MODIFIED Requirements

### Requirement: A store declares a format level

The system SHALL resolve every connection store to a format level before reading the store's
contents. A store carrying **no** level declaration SHALL resolve to classic, a store declaring a
recognised level SHALL resolve to that level, and a store declaring a level this build does not
recognise SHALL NOT resolve at all — it is refused rather than read.

The declaration and the resolved level are separate things: a store need not carry a declaration, and
one that carries an unreadable declaration has no level rather than a default one.

The fork writes to `%APPDATA%\mRemoteNG\confCons.xml` — the path and filename upstream mRemoteNG
uses. Two applications therefore read one file, and the reader has to know which format it is looking
at before it can decide how to open it.

Absence and non-recognition are different facts. Absence is what every file written before the level
existed looks like, and what every file upstream mRemoteNG writes looks like; reading those as
classic is correct and must not change. A level that is present but unrecognised says the opposite —
that a build which knew more than this one wrote the file deliberately. Reading it as classic
discards that statement, and the next ordinary save writes the file back without it.

The system already refuses a SQL database newer than it understands rather than reading it as
plausible nonsense. This extends the same treatment to the connection file, which does not have it.

#### Scenario: A store written by upstream or an earlier build

- **WHEN** a store carries no level declaration
- **THEN** it is treated as classic

#### Scenario: A store written at the hardened level

- **WHEN** a store declares the hardened level
- **THEN** it is read using the hardened format

#### Scenario: A store written at an unrecognised level

- **WHEN** a store declares a level this build does not recognise
- **THEN** the store is refused before any decryption is attempted
- **AND** no password is requested for it
- **AND** the user is told the file was written by a newer build

Refused before authentication, not after. A password prompt on a file that was never going to open
teaches the user their password is wrong, which is the same misdiagnosis a hardened file produces in
upstream mRemoteNG — and the reason that failure mode is worth avoiding here.

#### Scenario: An unrecognised level is never written back as classic

- **WHEN** a store declares a level this build does not recognise
- **THEN** the system SHALL NOT write that store at the classic level
- **AND** the declared level is left as it was found
