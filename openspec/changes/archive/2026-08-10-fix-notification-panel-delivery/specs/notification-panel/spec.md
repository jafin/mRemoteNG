## ADDED Requirements

### Requirement: Panel entries appear in the order they were reported

The notifications panel SHALL show entries in the order their messages were reported, whichever
thread reported them.

The panel inserts each entry at the top, so order depends on the order entries reach it. Messages
reported from a background thread were queued to the UI thread while messages reported by the UI
thread went straight in, letting a later message land above an earlier one — contradicting the
timestamps the panel displays.

#### Scenario: A background message and a UI message reported in that order

- **WHEN** a message is reported from a background thread
- **AND** a further message is reported by the UI thread before the first has been rendered
- **THEN** the panel shows the background message below the later one, matching their timestamps

#### Scenario: Messages reported during rendering

- **WHEN** a message is reported while earlier messages are still being rendered
- **THEN** it is rendered after them, not among them

### Requirement: Messages can be reported from any thread

Reporting a message SHALL be safe from any thread, including before the panel has a window to render
into.

Messages arrive from background workers and the UI thread alike. The buffer holding them until the
panel had a handle was an unsynchronized list mutated in place by every caller, so concurrent
reports during startup could corrupt it, lose messages, or subscribe to the handle twice — and a
concurrently mutated list fails later and elsewhere, not where the mistake was made.

#### Scenario: Concurrent reporters

- **WHEN** several threads report messages at the same time
- **THEN** every message is retained
- **AND** each is rendered exactly once

#### Scenario: Reporting before the panel has a window

- **WHEN** messages are reported before the panel's handle exists
- **THEN** they are held
- **AND** rendered in order once it does

#### Scenario: Exactly one rendering pass is outstanding

- **WHEN** several messages are reported before any is rendered
- **THEN** one rendering pass is scheduled, not one per message

#### Scenario: The panel goes away

- **WHEN** the panel is disposed with messages still queued
- **THEN** they are discarded without error
