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

#### Scenario: The periodic-save setting has never been chosen

- **WHEN** the application exits with `SaveConnectionsFrequency` still unassigned, as it ships
- **THEN** the setting that the options page migrates it from decides whether to save on exit
- **AND** an unassigned setting does not mean "never save on exit"

### Requirement: Every persisted change to the root node is an edit

The system SHALL raise a change notification when a persisted property of the connection file's root
node is changed, so that the on-edit save applies to it as it does to every other property.

`RootNodeInfo.Password` was a plain auto-property hiding the base property that does notify, as were
the password value itself, TOTP, auto-lock and the root's name. Changing the master password
therefore requested no save at all — not a save that was deferred and lost, but one that was never
asked for. Ordinary edits still saved, which is what made the master password look uniquely broken.

#### Scenario: Setting or clearing the master password

- **WHEN** password protection on the connection file is turned on or off
- **THEN** the change is saved in the same way as any other edit

#### Scenario: Changing the master password to a different one

- **WHEN** the password value changes but the file stays protected
- **THEN** the change is saved, because the file has to be rewritten under the new key

#### Scenario: A property set to the value it already had

- **WHEN** a root property is assigned its current value
- **THEN** no change is notified and no save is requested

### Requirement: An explicit save writes immediately

The system SHALL perform File > Save Connections at once rather than deferring it, and SHALL NOT
leave a superseded debounced save armed to write the same state again.

Save is an instruction, not an edit notification. Debounced, it let the user invoke Save, close the
application, and lose the change they had just asked to have stored.

#### Scenario: Saving and closing straight away

- **WHEN** the user invokes Save and closes the application immediately
- **THEN** the file has already been written

### Requirement: A save that does not happen is visible without the log

The system SHALL make a skipped or failed connection save apparent to the user, and SHALL NOT leave
the log as the only trace.

There are four ways for a change not to reach disk — no model or file name, no loaded connection file,
a batch in progress, or a thrown exception — and all of them are currently silent. The interface
behaves as though the change was stored, which is how a master password came to be believed set while
the file kept the old one.

A failure must also survive the trip up to whoever can report it. Both layers under the service
caught every exception and returned normally, so the service raised its saved event and logged
success over a file that had not been written.

#### Scenario: A save fails

- **WHEN** writing the connection file throws
- **THEN** the failure is surfaced to the user
- **AND** the message says the change was not saved
- **AND** the save is not also reported as having succeeded

#### Scenario: A batch is left open by an error

- **WHEN** an operation that defers saves does not complete normally
- **THEN** deferral ends with it, rather than swallowing every later save for the rest of the session

#### Scenario: A deferred request is not replayed

- **WHEN** a batch ends and its deferred save is performed
- **THEN** a later batch that defers nothing performs no save

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
