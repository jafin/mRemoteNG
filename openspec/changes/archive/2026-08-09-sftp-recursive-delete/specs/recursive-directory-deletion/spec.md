## ADDED Requirements

### Requirement: A directory is deleted with its contents

The file manager SHALL delete a selected directory together with everything beneath it, on both the
local and the remote side. It SHALL delete children before their parent, so a directory is removed only
once it is empty.

Deleting a folder is what the user asked for; being told the server refused because the folder is not
empty describes an implementation, not an answer. Ordering is not a detail — a parent removed before its
children is a request the server rejects, so the order is what makes the operation work at all.

#### Scenario: Deleting a directory with files in it

- **WHEN** the user deletes a directory containing files
- **THEN** the files are deleted
- **AND** the directory is deleted after them
- **AND** it is gone from the listing once the queue drains

#### Scenario: A nested tree

- **WHEN** the deleted directory contains subdirectories several levels deep
- **THEN** every entry beneath it is deleted
- **AND** each directory is deleted only after its own contents

#### Scenario: An empty directory

- **WHEN** the user deletes an empty directory
- **THEN** it is deleted

#### Scenario: A single file

- **WHEN** the user deletes a file rather than a directory
- **THEN** it is deleted as before

#### Scenario: A mixed selection

- **WHEN** the user deletes several entries at once, some files and some directories
- **THEN** every selected entry and everything beneath the directories is deleted

### Requirement: Deletions are queued and can be watched and cancelled

The system SHALL add each deletion to the transfer queue rather than performing them in a blocking loop.
Each SHALL appear as its own row identifying what is being deleted, and SHALL be subject to the queue's
existing rules: one at a time, individually cancellable, cancellable as a whole, and one failure not
stopping the rest.

This is what makes recursive deletion acceptable rather than reckless. The reason the file manager
refused to recurse was that a single click would destroy a tree the user could not see or stop; a queue
of visible rows that can be cancelled is the answer to exactly that.

#### Scenario: Watching a deletion

- **WHEN** a directory's contents are being deleted
- **THEN** the queue shows the entries being deleted and their progress through the work

#### Scenario: Cancelling the remainder

- **WHEN** the user cancels the whole queue during a deletion
- **THEN** the entries not yet deleted are not deleted

#### Scenario: The file manager stays usable

- **WHEN** a large tree is being deleted
- **THEN** the user can continue browsing while it runs

#### Scenario: A deletion row has no destination

- **WHEN** a deletion appears in the queue
- **THEN** it is distinguishable from a transfer
- **AND** it shows no destination

#### Scenario: One failure does not stop the rest

- **WHEN** one entry cannot be deleted
- **THEN** the failure is recorded against that row
- **AND** the remaining entries are still attempted

### Requirement: A recursive deletion is confirmed once

The system SHALL ask for confirmation once before deleting a directory's contents, naming what is being
deleted and making clear that the contents are included. It SHALL NOT ask again per entry. If the user
declines, nothing SHALL be deleted and nothing SHALL be queued.

One prompt per file is a prompt people learn to click through, which is worse than not asking at all.
One prompt that says what is about to happen is the only one that gets read.

#### Scenario: Confirming

- **WHEN** the user deletes a directory with contents
- **THEN** they are asked once
- **AND** the question makes clear the contents are included

#### Scenario: Declining

- **WHEN** the user declines the confirmation
- **THEN** nothing is deleted
- **AND** nothing is added to the queue

#### Scenario: Not asked twice

- **WHEN** the confirmed deletion covers many entries
- **THEN** the user is not asked again for any of them

### Requirement: Links are deleted, not followed

The system SHALL delete a symbolic link or reparse point as an entry in its own right, and SHALL NOT
descend into it or delete what it points at.

Following a link when deleting destroys data outside the tree the user selected — the one mistake in this
feature that cannot be walked back. It is also what would let a link to an ancestor turn a folder
deletion into an unbounded one.

#### Scenario: A link inside the tree

- **WHEN** the deleted tree contains a symbolic link to a directory
- **THEN** the link is deleted
- **AND** the directory it points at is not

#### Scenario: A link is not descended into

- **WHEN** the deleted tree contains a link pointing at one of its own ancestors
- **THEN** the deletion terminates
- **AND** only entries beneath the selected directory are deleted

### Requirement: An unreadable part of a tree does not abandon the rest

The system SHALL report a subdirectory it cannot list and continue deleting the rest of the tree. A
directory whose contents were not all deleted SHALL be reported when its own deletion fails, and SHALL
NOT be removed by any other means.

Carrying on with the branches that can be deleted is what the queue already does for transfers. Forcing
a parent whose children survived would delete something the system was just told it could not read,
which is the opposite of what the failure means.

#### Scenario: A subdirectory that cannot be listed

- **WHEN** listing a subdirectory fails during enumeration
- **THEN** the failure is reported
- **AND** the other branches are still deleted

#### Scenario: A parent whose children remain

- **WHEN** an entry could not be deleted
- **AND** its parent directory is then attempted
- **THEN** the parent's deletion fails and is reported
- **AND** the parent is left in place
