## ADDED Requirements

### Requirement: A directory can be transferred with everything beneath it

The file manager SHALL transfer a selected directory together with every file beneath it, in both
directions, without the user selecting the files individually. The transfer SHALL reproduce the source
tree's structure under the destination directory.

Transferring a tree is the common case the file manager currently cannot do at all, and doing it by
hand means recreating every folder on the far side before the files will go anywhere.

#### Scenario: Uploading a directory

- **WHEN** the user transfers a local directory to the remote pane
- **THEN** every file beneath it is queued for upload
- **AND** each file's destination path mirrors its path relative to the selected directory
- **AND** the selected directory itself appears under the remote pane's current directory

#### Scenario: Downloading a directory

- **WHEN** the user transfers a remote directory to the local pane
- **THEN** every file beneath it is queued for download
- **AND** each file's destination path mirrors its path relative to the selected directory

#### Scenario: Nested directories

- **WHEN** the transferred directory contains subdirectories several levels deep
- **THEN** files at every level are queued
- **AND** each keeps its position in the tree at the destination

#### Scenario: A mixed selection

- **WHEN** the user selects both files and directories and transfers them together
- **THEN** the files are queued as before
- **AND** each directory is expanded and its contents queued as well

#### Scenario: Dragging a folder from the desktop

- **WHEN** the user drags a folder from Explorer onto the remote pane
- **THEN** the folder's whole tree is queued for upload
- **AND** it is no longer reported as unsupported

### Requirement: Destination directories are created as needed

The system SHALL create any destination directory that does not already exist before writing a file
into it, and SHALL create directories that are empty in the source so that the destination tree
matches the source tree. Creating a directory that already exists SHALL NOT be treated as a failure.

Without this, every file in a tree that has no counterpart folder on the far side would fail, which is
exactly the manual work this change exists to remove.

#### Scenario: Missing destination directories

- **WHEN** a queued file's destination directory does not exist
- **THEN** it and any missing ancestors are created before the file is written

#### Scenario: An existing destination directory

- **WHEN** a queued file's destination directory already exists
- **THEN** the file is written into it
- **AND** no failure is reported

#### Scenario: An empty source directory

- **WHEN** the transferred tree contains a directory with no files beneath it
- **THEN** the corresponding destination directory is still created

#### Scenario: A destination directory that cannot be created

- **WHEN** creating a destination directory fails
- **THEN** the failure is reported
- **AND** the files beneath it are not queued, rather than queued to fail one by one
- **AND** the rest of the tree is still expanded and queued

### Requirement: Existing destination files are resolved by one choice per transfer

The system SHALL detect when a file would replace an existing file at the destination, and SHALL ask
the user how to resolve such collisions. It SHALL offer overwriting every colliding file, skipping
every colliding file, overwriting only where the source is newer than the destination, and cancelling
the transfer. The system SHALL ask at most once per transfer and SHALL apply the answer to every
collision in that transfer without asking again. It SHALL NOT ask when the transfer has no collisions.

A directory that already exists at the destination SHALL NOT be treated as a collision; the source
directory's contents are merged into it.

Asking per file is what makes a tree transfer unusable — a hundred prompts is a hundred chances to
click the wrong one. Not asking at all is what the file manager does today, and it means a transfer
can destroy work at the destination with no warning.

#### Scenario: A transfer with no collisions

- **WHEN** no file being transferred already exists at the destination
- **THEN** the user is not asked anything
- **AND** every file is queued

#### Scenario: The first collision asks once

- **WHEN** several files being transferred already exist at the destination
- **THEN** the user is asked once
- **AND** is not asked again for the remaining collisions in the same transfer

#### Scenario: Overwrite all

- **WHEN** the user chooses to overwrite
- **THEN** every colliding file is queued and replaces the file at the destination

#### Scenario: Skip all

- **WHEN** the user chooses to skip
- **THEN** no colliding file is queued
- **AND** the number of files skipped is reported
- **AND** files that do not collide are still queued

#### Scenario: Overwrite only where the source is newer

- **WHEN** the user chooses to overwrite only where the source is newer
- **AND** a colliding file's source is newer than its destination
- **THEN** that file is queued

#### Scenario: The destination is newer

- **WHEN** the user chooses to overwrite only where the source is newer
- **AND** a colliding file's destination is the same age or newer than its source
- **THEN** that file is not queued
- **AND** it is counted among the files skipped

#### Scenario: Cancelling at the prompt

- **WHEN** the user cancels at the prompt
- **THEN** no further items are queued for that transfer
- **AND** any expansion still running stops

#### Scenario: An existing destination directory is not a collision

- **WHEN** a directory being transferred already exists at the destination
- **THEN** the user is not asked about it
- **AND** the source directory's contents are transferred into it

#### Scenario: A file colliding with a directory

- **WHEN** a file being transferred has the same name as a directory at the destination
- **THEN** it is reported and skipped
- **AND** the user is not asked to overwrite it

#### Scenario: A later transfer asks again

- **WHEN** the user starts a second transfer that also has collisions
- **THEN** they are asked again
- **AND** the earlier answer is not reused

### Requirement: Links are not followed when expanding a tree

The system SHALL NOT descend into a symbolic link or a Windows reparse point, on either side. A link
that resolves to a directory SHALL be reported and skipped. A link that resolves to a file SHALL be
transferred as an ordinary file. The system SHALL additionally enforce a maximum expansion depth and
stop descending beyond it, reporting that it did so.

A link pointing at its own ancestor makes the tree infinite. Following one would fill the queue until
the application ran out of memory, and the depth limit is the backstop for any cycle a link check does
not catch. A listing alone cannot tell the two kinds of link apart — an SFTP listing reports a link
without following it — so a link is resolved before it is classified.

#### Scenario: A link to a directory

- **WHEN** the transferred tree contains a symbolic link that resolves to a directory
- **THEN** the link is not descended into
- **AND** it is reported as skipped

#### Scenario: A link pointing at an ancestor

- **WHEN** a directory beneath the transferred tree links back to one of its own ancestors
- **THEN** expansion terminates
- **AND** the queue does not grow without bound

#### Scenario: The depth limit is reached

- **WHEN** the tree is deeper than the maximum expansion depth
- **THEN** expansion stops descending at that depth
- **AND** the system reports that the tree was not expanded in full

#### Scenario: A link to a file

- **WHEN** the transferred tree contains a symbolic link that resolves to a file
- **THEN** its contents are transferred as an ordinary file

### Requirement: Expansion runs in the background and can be cancelled

The system SHALL walk a directory tree without blocking the user interface, SHALL add items to the
transfer queue as they are discovered rather than only once the walk has finished, and SHALL stop the
walk when the user cancels the queue or closes the file manager.

Walking a large tree over a slow link takes far longer than any interaction should. Running it on the
interface thread would freeze the tab — including the pane the user is looking at — and holding every
item back until the walk finished would leave the queue empty while it happened.

#### Scenario: Expanding a large tree

- **WHEN** the user transfers a directory containing many files
- **THEN** the file manager stays responsive while the tree is walked
- **AND** items appear in the queue as they are found

#### Scenario: Cancelling during expansion

- **WHEN** the user cancels the whole queue while a tree is still being walked
- **THEN** the walk stops
- **AND** no further items are added

#### Scenario: Closing during expansion

- **WHEN** the file manager is closed while a tree is still being walked
- **THEN** the walk stops

### Requirement: An unreadable part of a tree does not abandon the rest

The system SHALL report a subdirectory it cannot list and continue expanding the rest of the tree.
Failure to expand one branch SHALL NOT cancel the transfer of any other branch.

One protected subdirectory in a large tree is normal, and abandoning the whole transfer because of it
would make the feature unusable exactly where it is most useful.

#### Scenario: A subdirectory that cannot be listed

- **WHEN** listing a subdirectory fails because access is denied
- **THEN** the failure is reported
- **AND** the remaining branches are still expanded and queued

#### Scenario: A subdirectory that vanishes mid-walk

- **WHEN** a subdirectory is removed while the tree is being walked
- **THEN** the failure is reported
- **AND** expansion continues

### Requirement: Expanded items behave as ordinary queue items

Files discovered by expanding a directory SHALL be queued as ordinary transfer items, subject to the
existing queue behaviour: individual and whole-queue cancellation, per-item progress, and one item's
failure not stopping the rest.

Expansion decides *what* to transfer; it does not introduce a second kind of transfer with its own
rules. Anything else would mean the queue behaved differently depending on how an item got there.

#### Scenario: Progress on an expanded item

- **WHEN** a file queued by expanding a directory is transferring
- **THEN** its progress is shown the same way as a directly queued file

#### Scenario: Cancelling one expanded item

- **WHEN** the user cancels a single item that came from an expanded directory
- **THEN** that item stops
- **AND** the other items from the same directory continue

#### Scenario: One expanded item fails

- **WHEN** transferring one file from an expanded directory fails
- **THEN** the failure is recorded against that item
- **AND** the remaining files from the same directory are still transferred
