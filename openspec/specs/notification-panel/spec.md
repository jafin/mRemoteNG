# notification-panel Specification

## Purpose
TBD - created by archiving change fix-notification-panel-visibility. Update Purpose after archive.
## Requirements
### Requirement: No message is lost to writer registration order

A message added to the collector before any writer is attached SHALL still be delivered to every
writer once they are attached. Delivery SHALL happen exactly once per writer.

Today `MessageCollector` raises `CollectionChanged` and nothing retains the backlog, so anything
reported before `FrmMain_Load` attaches the writers is discarded by every writer at once — the
panel, the popup writer and the log file. That window covers settings loading and settings upgrade,
whose failures are reported as errors and are exactly what a user needs to see.

#### Scenario: Messages predating writer attachment are delivered

- **WHEN** messages are added to the collector before any writer is attached
- **AND** writers are then attached
- **THEN** each writer receives every message added beforehand
- **AND** receives them in the order they were added

#### Scenario: Messages are not delivered twice

- **WHEN** writers are attached to a collector holding earlier messages
- **AND** a further message is added afterwards
- **THEN** each earlier message is delivered exactly once
- **AND** the later message is delivered exactly once

#### Scenario: Attaching to an empty collector

- **WHEN** writers are attached to a collector holding no messages
- **THEN** no message is delivered
- **AND** subsequent messages are delivered normally

#### Scenario: A settings failure during startup is reported

- **WHEN** loading settings fails before writers are attached
- **THEN** the resulting error message reaches the writers once they are attached

### Requirement: Informational and warning messages are shown in the panel by default

The notifications panel SHALL show debug, informational, warning and error messages according to
user-configurable filters, and the shipped defaults SHALL enable informational and warning messages.

The text log writer already ships with both enabled. Shipping the panel with them disabled meant the
product retained these messages but showed the user only errors, so any feature that reports a
recoverable condition — an unusable credential, a skipped key, a degraded fallback — reported into a
panel that was not displaying it.

#### Scenario: Default panel filters

- **WHEN** the notification filter settings have never been changed
- **THEN** informational, warning and error messages are shown in the panel
- **AND** debug messages are not

#### Scenario: Filters remain configurable

- **WHEN** the user disables informational messages for the panel
- **THEN** informational messages are no longer shown in the panel
- **AND** other message classes are unaffected

#### Scenario: Log-only messages stay out of the panel

- **WHEN** a message is added with the log-only flag set
- **THEN** it is written to the log
- **AND** it is not shown in the notifications panel, whatever the filters allow

### Requirement: Panel entries show when they were reported

Each notifications panel entry SHALL display the time its message was recorded, taken from the
message rather than from the time it was rendered.

`IMessage.Date` is already populated at construction. Rendering time would be wrong for any message
delivered from the backlog, which is precisely the startup messages this change recovers.

#### Scenario: An entry shows its timestamp

- **WHEN** a message is shown in the notifications panel
- **THEN** the entry displays the time the message was recorded

#### Scenario: A replayed message keeps its original time

- **WHEN** a message added before writer attachment is delivered from the backlog
- **THEN** its entry displays the time it was originally recorded, not the time it was delivered

### Requirement: The collected message list is safe to read while messages arrive

Reading the collected messages SHALL NOT throw when another thread adds a message during the read.

Messages arrive from background workers as well as the UI thread. The collector guards its own
mutations but hands out its live backing list, so any caller enumerating it races with those writers.

#### Scenario: Reading during concurrent writes

- **WHEN** the collected messages are enumerated
- **AND** another thread adds messages during that enumeration
- **THEN** the enumeration completes without throwing

