## ADDED Requirements

### Requirement: The log level is configurable and defaults to information

The system SHALL take its minimum log level from a user setting, SHALL default that setting to
information, and SHALL apply a change without requiring a restart.

The level is currently a constant in the only place the logger is constructed, so every user writes
every debug message of every subsystem to disk on every run. Diagnostic depth is something a person
troubleshooting should choose, not something everyone pays for permanently.

#### Scenario: Default installation

- **WHEN** the application starts with no log level configured
- **THEN** messages below information are not written

#### Scenario: Raising the level for troubleshooting

- **WHEN** the user selects verbose logging
- **THEN** verbose messages are written from that point on
- **AND** no restart is required

#### Scenario: The log path changes

- **WHEN** the log path changes and the logger is rebuilt
- **THEN** the configured level is preserved

### Requirement: The command line is sanitised wherever it is logged

The system SHALL apply the same redaction to the command line everywhere it is recorded.

`DebugReportBuilder` already replaces the user profile path before including the command line; the
startup logger writes the same data unredacted. One of the two is wrong, and the one that redacts is
the one to keep.

#### Scenario: Logging the command line at startup

- **WHEN** the command line is written to the log at startup
- **THEN** the user profile path is replaced with a placeholder
- **AND** hostnames and accounts are left intact

The startup log and the debug report deliberately redact to different strengths, so they do not
produce the same text and the requirement must not ask them to. The log is the user's own file, read
to work out what went wrong, and stripping the hostnames from one line while every connection message
below still carries them would cost the log its use and hide nothing. Path redaction is the part they
share, because a profile path carries the Windows account name and nothing being logged needs it.

#### Scenario: A new caller logs the command line

- **WHEN** any component records the command line
- **THEN** it uses the shared sanitiser rather than formatting the arguments itself

### Requirement: Secrets are never written to the log

The system SHALL NOT write connection passwords, credential passwords, key passphrases or stored
secrets to the application log at any level, including verbose.

The log has no access control beyond the file system and is routinely attached to bug reports. A
verbose level that a user can now enable deliberately must not become a way to extract credentials
from their own installation.

#### Scenario: Verbose logging is enabled

- **WHEN** the log level is verbose and a connection authenticates
- **THEN** no password, passphrase or stored secret appears in the log

#### Scenario: A credential fails to load

- **WHEN** a credential cannot be used and the failure is logged
- **THEN** the message identifies the credential by name or path
- **AND** does not include its value
