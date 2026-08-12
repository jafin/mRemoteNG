# storage-format-compatibility Specification

## Purpose
TBD - created by archiving change add-storage-format-opt-in. Update Purpose after archive.
## Requirements
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

### Requirement: A classic store stays readable by upstream mRemoteNG

The system SHALL NOT write any construct upstream mRemoteNG cannot read into a store at the classic
level.

This is the property that lets a user evaluate the fork and go back. It has to be a rule the code
enforces rather than a habit, because every one of the hardening changes is individually tempting to
apply on next save.

#### Scenario: Saving a classic store

- **WHEN** a store at the classic level is saved
- **THEN** the key derivation, the key source and the stored format are those upstream understands
- **AND** no attribute, sentinel or version unknown to upstream is written

#### Scenario: A hardening feature is configured but the store is classic

- **WHEN** a hardening option is enabled and the store is at the classic level
- **THEN** the store is still written in the classic format
- **AND** the setting takes effect only once the level is raised

### Requirement: A store is never upgraded without explicit confirmation

The system SHALL raise a store's format level only when the user confirms it for that store, and
SHALL NOT raise it as a consequence of saving, editing, importing, or upgrading the application.

Upgrading a shared store removes the user's ability to return to the application they came from, and
for a SQL database removes it for colleagues who never installed this fork. A change that cannot be
undone by the person affected must not happen as a side effect of ordinary work.

#### Scenario: Ordinary use of a classic store

- **WHEN** a classic store is opened, edited and saved
- **THEN** its level is unchanged
- **AND** no upgrade prompt interrupts the save

#### Scenario: The application is upgraded

- **WHEN** a newer build of the application opens a classic store
- **THEN** the store stays classic
- **AND** the decision is not made by the application version

#### Scenario: The user confirms

- **WHEN** the user asks to raise the level and confirms
- **THEN** the store is rewritten in the hardened format

### Requirement: The confirmation states which applications will stop working

The confirmation SHALL name upstream mRemoteNG and earlier builds of this fork as unable to open the
store afterwards, SHALL state that existing backups stay readable but new ones will not, and SHALL
present the classic export as the way back.

Upstream does not fail gracefully on a hardened store: an unknown key derivation attribute is
ignored and produces a decryption failure reported as a wrong password. A user who upgraded and went
back would be asked for a password they know, on a file that will not accept it. The warning has to
carry what the failure will not.

#### Scenario: Offering the upgrade

- **WHEN** the upgrade is offered
- **THEN** the message names upstream mRemoteNG and earlier builds of this fork
- **AND** states that backups written afterwards will not be readable by them
- **AND** points to the classic export

#### Scenario: Upgrading a SQL database

- **WHEN** the store is a SQL database
- **THEN** the message additionally states that every client must be upgraded

### Requirement: A hardened store can be exported in the classic format

The system SHALL export a hardened store to a classic-format file readable by upstream mRemoteNG, and
SHALL state that the exported copy has the weaker protection.

Without a way back, the confirmation is asking the user to make an irreversible decision on trust. An
export makes the upgrade reversible, which is what makes it reasonable to offer at all.

#### Scenario: Exporting

- **WHEN** a hardened store is exported in the classic format
- **THEN** the result opens in upstream mRemoteNG
- **AND** the user is told its protection is weaker than the store it came from

#### Scenario: The exported file round-trips

- **WHEN** a classic export is opened by this fork
- **THEN** it opens as a classic store
- **AND** it is not silently re-hardened

### Requirement: Hardening is offered once per connection file

The system SHALL offer the upgrade once for each connection file at the classic level, SHALL allow
the offer to be dismissed, SHALL NOT show it again for a file that has been dismissed, and SHALL NOT
offer it for a shared store.

A security feature nobody discovers is one that shipped for nothing, and a repeated prompt about a
decision already made deliberately teaches users to dismiss everything the application says —
including the messages that matter. Offering once treats a dismissal as the answer to a question
rather than as something to ask again next session.

It is limited to the connection file because that is the store where the person prompted is the
person affected. A shared store's upgrade is decided by whoever administers it, not by whoever opens
the application first.

#### Scenario: A classic connection file is opened for the first time

- **WHEN** a connection file at the classic level is opened and the offer has not been dismissed for it
- **THEN** the upgrade is offered
- **AND** the offer can be dismissed without upgrading

#### Scenario: The offer was dismissed

- **WHEN** a connection file whose offer was dismissed is opened again
- **THEN** the offer is not shown
- **AND** the upgrade remains reachable on request

#### Scenario: A second connection file

- **WHEN** the user opens a different classic connection file
- **THEN** the offer is made for that file
- **AND** a dismissal recorded against another file does not suppress it

#### Scenario: Dismissal survives a reinstall

- **WHEN** the application is reinstalled and a previously dismissed connection file is opened
- **THEN** the offer is still not shown

#### Scenario: A shared store

- **WHEN** a SQL database at the classic level is opened
- **THEN** no upgrade offer is shown
- **AND** the upgrade remains reachable from the SQL configuration

### Requirement: The store's format level is visible

The system SHALL show the current store's format level where a user can find it without opening
options.

A protection nobody knows about is a protection nobody enables, and a compatibility guarantee nobody
can see is one they cannot rely on when deciding whether to try another build.

#### Scenario: Using a classic store

- **WHEN** a classic store is open
- **THEN** its level is discoverable without opening options

#### Scenario: Using a hardened store

- **WHEN** a hardened store is open
- **THEN** its level is discoverable without opening options

