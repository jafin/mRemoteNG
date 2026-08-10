## ADDED Requirements

### Requirement: An edit the user has made reaches disk before the application exits

The system SHALL write any pending connection change before the process ends, and SHALL NOT let the
periodic-save setting decide whether an edit already made is written.

Saves are debounced by two seconds so that rapid property changes coalesce into one write rather than
one per event, each re-deriving a key at 600,000 iterations. The debounce runs on a thread-pool timer
that keeps nothing alive, so closing the application inside that window discards the write. Nothing
flushes it, and shutdown's own save is conditional on `SaveConnectionsFrequency` — a setting about how
often to save periodically, not about whether the user's last action survives.

#### Scenario: Closing immediately after an edit

- **WHEN** a connection is changed and the application is closed before the debounce elapses
- **THEN** the change is written before the process exits

#### Scenario: Closing with no pending change

- **WHEN** the application is closed and nothing is pending
- **THEN** no additional write occurs

#### Scenario: The periodic-save setting is not "on exit"

- **WHEN** an edit is pending and `SaveConnectionsFrequency` is set to anything other than on-exit
- **THEN** the pending edit is still written

#### Scenario: The flush cannot hang shutdown

- **WHEN** the write cannot complete
- **THEN** shutdown proceeds rather than waiting indefinitely
- **AND** the failure is reported

### Requirement: A save that does not happen is visible without the log

The system SHALL make a skipped or failed connection save apparent to the user, and SHALL NOT leave
the log as the only trace.

There are four ways for a change not to reach disk — no model or file name, no loaded connection file,
a batch in progress, or a thrown exception — and all of them are currently silent. The interface
behaves as though the change was stored, which is how a master password came to be believed set while
the file kept the old one.

#### Scenario: A save fails

- **WHEN** writing the connection file throws
- **THEN** the failure is surfaced to the user
- **AND** the message says the change was not saved

#### Scenario: A save is skipped

- **WHEN** a save cannot proceed because no connection file is loaded
- **THEN** that is reported rather than returning silently

#### Scenario: Reporting does not become noise

- **WHEN** saves succeed
- **THEN** nothing is reported
- **AND** a failure does not interrupt the user with a modal dialog for each occurrence

### Requirement: Coalescing is preserved

The system SHALL continue to collapse rapid successive changes into a single write.

The debounce is not the defect and removing it would restore the behaviour it was added to fix:
without it, a bulk edit or a status monitor produces one full re-encryption of every password per
property change.

#### Scenario: Many changes in quick succession

- **WHEN** several connection properties change within the debounce window
- **THEN** one write occurs, not one per change
