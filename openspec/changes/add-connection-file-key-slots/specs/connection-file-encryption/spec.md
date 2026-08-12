## ADDED Requirements

### Requirement: The per-file key may be wrapped for more than one account

The system SHALL support a bounded number of machine-bound protectors on one connection file, each
wrapping the same per-file key, SHALL open the file using whichever of them yields a key that
decrypts the file's protection sentinel, and SHALL refuse a set larger than the bound before trying
any of them.

A protector unwrapping proves only that it was written by this account, not that it belongs to this
file. A protector copied from another file the same user owns unwraps perfectly and yields a
different key, and the file then opens onto contents that do not decrypt — with no prompt, and
nothing reported. The sentinel is the one ciphertext in the file whose plaintext is known in advance,
so it is what tells a right key from a merely readable protector.

The bound exists because the protectors are tried in turn: without one, a file declaring a hundred
thousand protectors is a file that takes minutes to refuse to open.

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

#### Scenario: A file written with no protector

- **WHEN** a connection file carrying no machine-bound protector is opened
- **THEN** the recovery password opens it as it did before
- **AND** no migration of the file is required

#### Scenario: A protector that unwraps but belongs to another file

- **WHEN** a protector unwraps to a key that does not decrypt the file's protection sentinel
- **THEN** that key is not accepted
- **AND** the remaining protectors are still tried

#### Scenario: More protectors than the bound

- **WHEN** a connection file declares more machine-bound protectors than the bound
- **THEN** the file is refused
- **AND** no protector is attempted

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

#### Scenario: Rekeying from the portable edition

- **WHEN** the portable edition rekeys a file
- **THEN** a new per-file key and a new recovery password are written
- **AND** no machine-bound protector is written

### Requirement: A save cannot undo a rekey it did not see

The system SHALL record a key generation on the connection file, SHALL change it only when the file
is rekeyed, and SHALL refuse a save whose key generation differs from the one on disk.

Ordinary saves are last-writer-wins and stay that way; nothing about them is a security boundary. A
rekey is. A member who had the file open before one still holds the previous key and the previous
recovery password, and their next save writes the contents back under both — reinstating access for
the person the rekey removed, silently, to a user who never knew a rekey happened.

Refusing a save is unpleasant. Reinstating a revoked member is worse, and only one of the two can be
recovered by the person it happens to.

#### Scenario: Saving after someone else rekeyed

- **WHEN** the file's key generation on disk differs from the one this session read
- **THEN** the save is refused
- **AND** the user is told the file was rekeyed and the store must be re-opened

#### Scenario: An ordinary concurrent save

- **WHEN** two sessions save a file whose key generation has not changed
- **THEN** neither save is refused on that ground

### Requirement: A lost protector costs a prompt and nothing else

The system SHALL treat the absence of an account's machine-bound protector as an ordinary fallback to
the recovery password, and the protector SHALL be restored when that account next supplies the
recovery password and saves.

Two people saving a shared file at once already loses one of the writes for the whole file. A
protector lost that way must not be a failure state: the account is prompted once more and its
protector is written again when it next saves. This is what makes locking unnecessary for ordinary
saves.

It is repaired by the member, not on its own — they must type the recovery password and then save,
which is the same flow as their first open. A member who never saves is never repaired and is
prompted every time, which is the same outcome the save-time rule produces everywhere else.

#### Scenario: A protector is lost to a concurrent save

- **WHEN** an account's machine-bound protector is absent from the file
- **THEN** the recovery password is requested
- **AND** the protector is written again once that account supplies it and saves
- **AND** no error is reported

#### Scenario: An empty protector set is not silently accepted

- **WHEN** a file records a machine-bound protector set that contains no protector
- **THEN** the file is refused
- **AND** it is not read as a file that was written without one
