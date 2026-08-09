# file-manager-reconnection Specification

## Purpose
TBD - created by archiving change sftp-panel-usability. Update Purpose after archive.
## Requirements
### Requirement: A dropped remote session can be reconnected from the tab

The file manager SHALL offer a reconnect action on the remote pane and on the tab itself, and SHALL
NOT offer one on the local pane. Both SHALL be available only while the session is disconnected, and
SHALL perform the same operation.

On success the system SHALL restore the tab's connected title and list the directory the pane was
showing when the connection dropped. On failure it SHALL report why and leave the pane disconnected,
with the action still available.

Today a drop is terminal: the only way back is to close the tab and open a new one, which loses the
directory the user was working in. Reconnecting in place is the difference between an interruption and
starting again.

#### Scenario: Reconnecting after a drop

- **WHEN** the remote session has dropped
- **AND** the user reconnects
- **THEN** the session is re-established
- **AND** the pane lists the directory it was showing

#### Scenario: The tab title recovers

- **WHEN** a reconnect succeeds
- **THEN** the tab stops reporting the connection as disconnected

#### Scenario: A failed reconnect

- **WHEN** a reconnect attempt fails
- **THEN** the reason is reported
- **AND** the pane remains disconnected
- **AND** the user can attempt it again

#### Scenario: The action is remote-only

- **WHEN** the file manager is open
- **THEN** the local pane offers no reconnect action

#### Scenario: The action is offered only when it applies

- **WHEN** the remote session is connected
- **THEN** the reconnect action is unavailable

#### Scenario: Reconnecting from the tab

- **WHEN** the remote session has dropped
- **AND** the user reconnects from the tab rather than the pane
- **THEN** the same reconnect is performed

### Requirement: Refreshing a disconnected pane attempts to reconnect

When the remote pane is refreshed while its session is disconnected, the system SHALL attempt to
reconnect before listing, and SHALL list the directory if the reconnect succeeds. If it fails, the
system SHALL report why and leave the previous listing on screen.

Refresh is the reflex when a pane looks wrong. Answering it with the same "not connected" error the
user has already seen makes them hunt for the real control; doing the obvious thing instead costs
nothing when the session is healthy, because a connected pane just lists as before.

#### Scenario: Refresh while disconnected

- **WHEN** the session has dropped
- **AND** the user refreshes the remote pane
- **THEN** a reconnect is attempted
- **AND** the directory is listed once it succeeds

#### Scenario: Refresh while connected

- **WHEN** the session is connected
- **AND** the user refreshes
- **THEN** the directory is listed without reconnecting

#### Scenario: Refresh when the reconnect fails

- **WHEN** the session has dropped and reconnecting fails
- **AND** the user refreshes
- **THEN** the failure is reported
- **AND** the pane continues to show the entries it already had

### Requirement: Only one reconnect attempt runs at a time

The system SHALL run at most one reconnect attempt at a time. A second request made while one is in
flight SHALL NOT start another.

The button and a refresh are two ways to ask for the same thing, and an impatient user will use both.
Two attempts in flight means two authentications and two clients where one is then abandoned.

#### Scenario: Asking twice

- **WHEN** a reconnect is in progress
- **AND** the user asks to reconnect again
- **THEN** no second attempt is started

#### Scenario: Reconnecting after an attempt finishes

- **WHEN** a reconnect attempt has finished and left the session disconnected
- **THEN** a further attempt can be started

### Requirement: Reconnecting does not abandon the previous connection

Re-establishing a session SHALL release the resources of the previous attempt before replacing them.

An SSH client holds a socket and its own threads. Replacing the reference without disposing it leaves
those alive for the life of the tab, so a user reconnecting through a flaky link accumulates one
abandoned client per attempt.

#### Scenario: Repeated reconnects

- **WHEN** a session is reconnected several times
- **THEN** each previous client and its authentication are disposed
- **AND** no connection from an earlier attempt is left open

