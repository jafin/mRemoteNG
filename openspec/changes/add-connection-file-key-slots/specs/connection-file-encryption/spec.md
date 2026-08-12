## ADDED Requirements

### Requirement: The per-file key may be wrapped for more than one account

The system SHALL support any number of machine-bound protectors on one connection file, each wrapping
the same per-file key, and SHALL open the file using whichever of them succeeds.

A file with a single machine-bound protector serves exactly one account. On a file several people
share, every other member falls back to the recovery password on every open and has no way to change
that, because there is nowhere to put a second protector. Several protectors is the arrangement full
disk encryption has used for two decades and it is what makes a shared file usable without making it
less protected — each protector wraps the same key and grants the same access the recovery password
already grants.

A single protector is one protector in this set, so nothing written before this requirement existed
changes meaning or needs migrating.

#### Scenario: A file written with one protector

- **WHEN** a connection file carrying a single machine-bound protector is opened
- **THEN** it opens as it did before
- **AND** no migration of the file is required

#### Scenario: Several accounts open one file

- **WHEN** a connection file carries a machine-bound protector for each of several accounts
- **THEN** each of those accounts opens the file without a prompt
- **AND** each obtains the same per-file key

#### Scenario: An account with no protector of its own

- **WHEN** an account opens a file that has machine-bound protectors, none of which is its own
- **THEN** the recovery password is requested
- **AND** supplying it decrypts the file

#### Scenario: A protector belonging to another account is present

- **WHEN** the set contains protectors that this account cannot unwrap alongside one that it can
- **THEN** the file opens without a prompt
- **AND** no failure is reported for the protectors that did not apply

### Requirement: An account that opened a file with the recovery password gains a protector on save

The system SHALL add a machine-bound protector for the current account when the file was opened with
the recovery password and the account has none, and SHALL do so when the file is next saved rather
than when it is opened.

Adding it on open would rewrite a file the user only read, which is the behaviour the per-file key
change already rejected, and on a shared file it would make every open a write to a file other people
have open.

An account that only ever reads therefore keeps being prompted. That is the correct outcome: it is
also the account for which writing to a shared file is least appropriate.

#### Scenario: A second account opens and then saves

- **WHEN** an account opens a shared file with the recovery password and later saves it
- **THEN** a machine-bound protector for that account is written
- **AND** subsequent opens by that account are not prompted

#### Scenario: A second account opens and does not save

- **WHEN** an account opens a shared file with the recovery password and does not save it
- **THEN** the file is not written
- **AND** the next open by that account requests the recovery password again

#### Scenario: The portable edition never gains a protector

- **WHEN** the portable edition opens a file with the recovery password and saves it
- **THEN** no machine-bound protector is written

### Requirement: Access is removed by rekeying, not by removing a protector

The system SHALL provide an operation that replaces the per-file key and the recovery password and
re-encrypts the file's contents, SHALL discard every existing machine-bound protector when it does,
and SHALL NOT offer removal of an individual protector as a way to withdraw access.

Removing one protector withdraws nothing. Anyone who had access knows the recovery password and has
had the opportunity to copy a file that lives on a share; the only operation that removes their
access to future contents is a new key. Offering protector removal would let a user believe they had
revoked access when they had not, which is worse than offering nothing.

#### Scenario: Rekeying a shared file

- **WHEN** the file is rekeyed
- **THEN** the contents are re-encrypted under a new per-file key
- **AND** a new recovery password is required
- **AND** every previously written machine-bound protector is discarded

#### Scenario: Other members after a rekey

- **WHEN** another account opens the file after a rekey
- **THEN** the new recovery password is requested
- **AND** the previous recovery password does not open the file

#### Scenario: What a rekey does not undo

- **WHEN** the user is asked to confirm a rekey
- **THEN** they are told that copies taken before it remain readable with the previous password

### Requirement: A lost protector costs a prompt and nothing else

The system SHALL treat the absence of an account's machine-bound protector as an ordinary fallback to
the recovery password, and the protector set SHALL converge without user action after concurrent
saves.

Two people saving a shared file at once already loses one of the writes for the whole file. A
protector lost that way must not be a failure state: the account is prompted once more and its
protector is written again on its next save. This is what makes locking unnecessary.

#### Scenario: A protector is lost to a concurrent save

- **WHEN** an account's machine-bound protector is absent from the file
- **THEN** the recovery password is requested
- **AND** the protector is written again when that account next saves
- **AND** no error is reported

#### Scenario: An empty protector set is not silently accepted

- **WHEN** a file records a machine-bound protector set that contains no protector
- **THEN** the file is refused
- **AND** it is not read as a file that was written without one
