## ADDED Requirements

### Requirement: A store loaded from a fallback copy is never written back to its source

The system SHALL refuse to save a connection store that was loaded from a local fallback copy rather
than from the source it stands in for, and SHALL report the refusal through the same path as any
other save that does not happen.

A cached copy records what one client saw at one moment. The source has since been changed by people
whose edits this client never saw, so writing the copy back does not restore a state that ever
existed: connections deleted elsewhere return, connections added elsewhere disappear, and every edit
made since the copy was taken is undone. It happens at the moment least likely to be suspected — the
database was unreachable, so nobody is watching for their colleagues' work to be reverted.

#### Scenario: Saving after a fallback load

- **WHEN** the store was loaded from a local fallback copy
- **AND** a save is requested, whether by the user or by the debounce timer
- **THEN** nothing is written to the source
- **AND** the refusal is reported where the user will see it without opening the log

#### Scenario: The source becomes reachable again

- **WHEN** the store is reloaded from its source successfully
- **THEN** saving is permitted again

#### Scenario: An ordinary load

- **WHEN** the store was loaded from its source
- **THEN** saving is unaffected

### Requirement: A fallback load says what it loaded and that changes will not be saved

The system SHALL state that the connections shown are a local copy, when that copy was taken, and
that changes will not be saved until the source is reachable — and SHALL NOT describe the state in
terms it does not enforce.

The application currently reports "Loading from local cache in read-only mode" while remaining fully
writable. A user told they are in read-only mode has been told the one thing that would stop them
editing, and it is not true.

#### Scenario: Falling back to a local copy

- **WHEN** the source cannot be read and a local copy is used
- **THEN** the user is told these connections are a local copy
- **AND** told how old it is
- **AND** told that changes will not be saved

#### Scenario: Describing the state

- **WHEN** the fallback is reported
- **THEN** it does not claim a restriction the application does not enforce
