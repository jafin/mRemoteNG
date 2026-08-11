# sftp-browser-panel Specification

## Purpose
TBD - created by archiving change add-sftp-browser-panel. Update Purpose after archive.
## Requirements
### Requirement: A file manager opens as its own tab for a connection

The system SHALL provide a file manager that opens as a tab for an SSH connection, showing the local
filesystem and the remote filesystem side by side with a transfer queue beneath them. Opening and
closing it SHALL NOT affect any session tab for the same connection.

A dual-pane layout with a queue below is the established shape for this tool, and it needs the full
width of a tab: two file listings and a queue with source, destination, size, speed and progress
columns do not fit usefully in a panel beside a live terminal.

#### Scenario: Opening the file manager

- **WHEN** the user opens the file manager for a connection
- **THEN** a tab opens showing a local pane, a remote pane and a transfer queue
- **AND** the remote pane connects and lists a directory

#### Scenario: A session tab is unaffected

- **WHEN** a session tab is open for the same connection
- **AND** the file manager is opened or closed
- **THEN** the session tab and its connection are unaffected

#### Scenario: Reopening

- **WHEN** the file manager is closed and reopened for the same connection
- **THEN** it reconnects and lists a directory again

#### Scenario: The remote connection drops

- **WHEN** the file manager's connection drops
- **THEN** it reports that it is no longer connected
- **AND** does not present stale directory contents as current

### Requirement: The local filesystem is browsable in its own pane

The file manager SHALL show the local filesystem in a pane with the same listing and navigation
behaviour as the remote pane, and SHALL allow the drive or starting folder to be chosen.

#### Scenario: Listing a local directory

- **WHEN** a local directory is opened
- **THEN** its entries are listed with name, size and modified time
- **AND** directories are distinguishable from files

#### Scenario: Navigating the local pane

- **WHEN** the user opens a local directory, or navigates up, back or forward
- **THEN** the local pane lists the requested directory

#### Scenario: A local directory that cannot be read

- **WHEN** listing a local directory fails because access is denied
- **THEN** the failure is reported
- **AND** the pane continues to show the last directory it listed

#### Scenario: Local and remote panes are independent

- **WHEN** the user navigates one pane
- **THEN** the other pane's location is unchanged

### Requirement: Transfers are managed through a queue

Transfers SHALL be added to a queue rather than run one at a time in a modal fashion. The queue SHALL
show each item's direction, source, destination, size, progress and status, SHALL allow an individual
item and the whole queue to be cancelled, and SHALL keep the file manager usable while it runs.

A queue is what makes the tool usable for real work: selecting twenty files and continuing to browse
is the normal case, and a single blocking transfer per action is what makes the existing transfer
window unusable for anything but a one-off.

#### Scenario: Queueing a transfer

- **WHEN** the user transfers a file between panes
- **THEN** an item is added to the queue with its direction, source, destination and size
- **AND** the file manager remains usable

#### Scenario: Queueing several transfers

- **WHEN** the user transfers several files at once
- **THEN** an item is added for each
- **AND** they are processed without the user waiting for one before queueing the next

#### Scenario: Progress is reported per item

- **WHEN** an item is transferring
- **THEN** its progress reflects the bytes moved so far

#### Scenario: A completed item

- **WHEN** an item finishes successfully
- **THEN** it is reported as successful
- **AND** the destination pane shows the transferred file after a refresh

#### Scenario: A failed item does not stop the queue

- **WHEN** an item fails
- **THEN** it is reported as failed with the reason
- **AND** the remaining items are still processed

#### Scenario: Cancelling one item

- **WHEN** the user cancels a queued or running item
- **THEN** that item stops
- **AND** the remaining items are still processed

#### Scenario: Cancelling the queue

- **WHEN** the user cancels the whole queue
- **THEN** the running item stops and no further items start

### Requirement: The panel authenticates using the connection's own credentials

The panel SHALL resolve credentials through `ISshCredentialResolver` for the connection it belongs
to, and SHALL NOT ask the user to re-enter credentials that the connection already supplies.

The existing transfer window makes the user retype the host, username and password that mRemoteNG
already holds. Repeating that in the panel would carry the defect forward into the feature meant to
replace it.

#### Scenario: Stored credentials are used

- **WHEN** the panel connects for a connection with stored credentials
- **THEN** those credentials authenticate the panel's connection
- **AND** the user is not prompted

#### Scenario: Provider-supplied credentials are used

- **WHEN** the connection uses an external credential provider
- **THEN** the provider is consulted for the panel's connection

#### Scenario: Agent identities are offered

- **WHEN** SSH agent support is enabled and the agent holds identities
- **THEN** those identities are offered when the panel authenticates

#### Scenario: The panel uses its own transport

- **WHEN** the panel connects
- **THEN** it establishes its own SSH connection
- **AND** the session's own connection is unaffected by the panel connecting or disconnecting

### Requirement: The panel lists and navigates the remote filesystem

The panel SHALL list the contents of a remote directory, distinguishing directories from files, and
SHALL support navigating into a directory, to the parent, to the home directory, and to a typed
path. It SHALL support returning to previously visited directories.

#### Scenario: Listing a directory

- **WHEN** a directory is opened
- **THEN** its entries are listed with name, size, modified time and permissions
- **AND** directories are distinguishable from files

#### Scenario: Navigating into a directory

- **WHEN** the user opens a directory entry
- **THEN** the panel lists that directory

#### Scenario: Navigating to the parent

- **WHEN** the user navigates up from a directory
- **THEN** the panel lists the parent directory

#### Scenario: Navigating back and forward

- **WHEN** the user has visited more than one directory
- **THEN** back returns to the previous directory
- **AND** forward returns to the one left by going back

#### Scenario: Navigating to a typed path

- **WHEN** the user enters a path
- **THEN** the panel lists that directory if it exists
- **AND** reports the failure without leaving the current listing if it does not

#### Scenario: Hidden entries are shown on request

- **WHEN** hidden entries are disabled
- **THEN** entries whose names begin with a dot are not listed
- **WHEN** hidden entries are enabled
- **THEN** they are listed

#### Scenario: A directory that cannot be read

- **WHEN** listing a directory fails because it cannot be read
- **THEN** the failure is reported
- **AND** the panel continues to show the last directory it listed

### Requirement: Files can be transferred in both directions with visible progress

The panel SHALL support uploading files to and downloading files from the remote host, SHALL report
progress while a transfer is running, and SHALL allow a running transfer to be cancelled.

Progress must come from the bytes actually moved. `SftpClient.UploadFileAsync` and
`DownloadFileAsync` expose no progress callback, so progress is counted from the stream, as
`ProgressReportingStream` already does for uploads.

#### Scenario: Uploading a file

- **WHEN** the user uploads a local file to the current remote directory
- **THEN** the file is written to the remote host
- **AND** the listing shows it once the transfer completes

#### Scenario: Downloading a file

- **WHEN** the user downloads a remote file
- **THEN** it is written to the chosen local location

#### Scenario: Progress is reported

- **WHEN** a transfer is running
- **THEN** progress reflects the bytes transferred so far

#### Scenario: Cancelling a transfer

- **WHEN** the user cancels a running transfer
- **THEN** the transfer stops
- **AND** the panel returns to a usable state

#### Scenario: A failed transfer

- **WHEN** a transfer fails
- **THEN** the failure is reported with the reason
- **AND** the panel remains connected and usable

#### Scenario: Dropping files onto the remote pane

- **WHEN** files are dragged from Windows Explorer onto the remote pane
- **THEN** they are queued for upload to the current remote directory

### Requirement: Remote files and directories can be managed

The panel SHALL support renaming and deleting entries, and creating a directory or an empty file in
the current directory. Deletion SHALL require confirmation.

#### Scenario: Renaming an entry

- **WHEN** the user renames an entry
- **THEN** it is renamed on the remote host
- **AND** the listing reflects the new name

#### Scenario: Deleting an entry

- **WHEN** the user deletes an entry and confirms
- **THEN** it is removed from the remote host
- **AND** the listing no longer shows it

#### Scenario: Deletion is confirmed first

- **WHEN** the user deletes an entry and does not confirm
- **THEN** nothing is removed

#### Scenario: Creating a directory

- **WHEN** the user creates a directory
- **THEN** it exists on the remote host and appears in the listing

#### Scenario: An operation that is refused

- **WHEN** an operation fails because the remote host refuses it
- **THEN** the reason is reported
- **AND** the listing is not changed to imply it succeeded

### Requirement: A remote file can be edited locally and written back

The panel SHALL support opening a remote file for editing by downloading it to a temporary location
and launching the local editor, and SHALL offer to upload the file back when its local copy changes.
The temporary copy SHALL be removed when editing finishes.

A remote file opened this way exists unencrypted on local disk for the duration. The same care the
credential work applies to temporary private key files applies here.

#### Scenario: Editing a remote file

- **WHEN** the user opens a remote file for editing
- **THEN** it is downloaded to a temporary location and opened in the local editor

#### Scenario: Saving the edited file

- **WHEN** the local copy changes
- **THEN** the user is offered the chance to upload it back
- **AND** the remote file is replaced if they accept

#### Scenario: Discarding the edit

- **WHEN** the user declines to upload the changed file
- **THEN** the remote file is unchanged

#### Scenario: The temporary copy is cleaned up

- **WHEN** editing finishes
- **THEN** the temporary local copy is removed

### Requirement: Remote operations do not block the user interface

Directory listings and transfers SHALL run without blocking the interface, and the panel SHALL
remain responsive while an operation is in progress.

A listing of a large directory over a slow link takes seconds. Running that on the UI thread would
freeze the whole application, including the session beside the panel.

#### Scenario: A slow listing

- **WHEN** a directory listing is in progress
- **THEN** the interface remains responsive

#### Scenario: A running transfer

- **WHEN** a transfer is in progress
- **THEN** the interface remains responsive
- **AND** the session beside the panel remains usable

