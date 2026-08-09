## ADDED Requirements

### Requirement: Provider-supplied keys are injected into the agent rather than written to disk

The system SHALL attempt to add provider-supplied private key material to a running SSH agent
instead of materialising it to a temporary file. Injection SHALL be attempted before any file is
written, so that the successful path never touches the filesystem.

#### Scenario: Key material is injected successfully

- **WHEN** an external credential provider returns private key material
- **AND** the material parses into a usable private key
- **AND** an agent that the target backend consumes is running
- **THEN** the key is added to that agent
- **AND** no temporary private key file is created

#### Scenario: The connection authenticates from the agent

- **WHEN** a key has been injected for a PuTTY-family connection
- **THEN** no `-i` argument is emitted for that key
- **AND** the connection relies on the agent to offer it

### Requirement: Injection failure falls back to the guarded temporary file

The system SHALL fall back to the existing guarded temporary-file path whenever injection is not
possible, and SHALL NOT fail the connection because injection was unavailable.

#### Scenario: Key material cannot be parsed

- **WHEN** provider-supplied key material is in a format the key parser does not support
- **THEN** the temporary-file path is used
- **AND** a diagnostic records that injection was skipped and why

#### Scenario: No agent is running

- **WHEN** no agent that the target backend consumes is reachable
- **THEN** the temporary-file path is used
- **AND** the connection attempt proceeds

#### Scenario: The agent refuses the key

- **WHEN** the agent rejects the add request
- **THEN** the temporary-file path is used
- **AND** the connection attempt proceeds

### Requirement: Injected keys are bounded in lifetime

The system SHALL add injected keys with an expiry rather than indefinitely, so a key cannot outlive
its purpose if removal does not run.

#### Scenario: An injected key expires

- **WHEN** a key is injected
- **THEN** it is added with a bounded lifetime

#### Scenario: An injected key is removed when the session ends

- **WHEN** the connection that triggered the injection closes
- **THEN** the injected key is removed from the agent
- **AND** failure to remove it is not treated as a connection error

### Requirement: The path taken is observable

The system SHALL record which key-delivery path a connection used, so the choice is diagnosable
without inference.

#### Scenario: Injection path reported

- **WHEN** a key is injected
- **THEN** an informational diagnostic records that the agent was used

#### Scenario: Fallback path reported

- **WHEN** the temporary-file fallback is used
- **THEN** an informational diagnostic records that a temporary file was written
