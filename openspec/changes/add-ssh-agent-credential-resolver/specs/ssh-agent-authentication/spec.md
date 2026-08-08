## ADDED Requirements

### Requirement: SSH agent identities are available as a credential source

The system SHALL provide an `ISshAgentProvider` that enumerates identities from a running SSH agent
and exposes them to the credential resolver. The provider SHALL support the Windows OpenSSH agent
named pipe and PuTTY Pageant.

#### Scenario: OpenSSH agent holds identities

- **WHEN** the Windows OpenSSH agent is running and holds at least one identity
- **AND** agent authentication is enabled for the connection
- **THEN** `ISshAgentProvider` returns those identities
- **AND** the resolved credential exposes them as agent identities

#### Scenario: Pageant holds identities

- **WHEN** PuTTY Pageant is running and holds at least one identity
- **AND** agent authentication is enabled for the connection
- **THEN** `ISshAgentProvider` returns those identities

#### Scenario: Pageant transport selection

- **WHEN** Pageant 0.77 or later is running
- **THEN** the provider communicates over the OpenSSH-compatible named pipe
- **WHEN** an earlier Pageant is running
- **THEN** the provider falls back to the `WM_COPYDATA` transport

### Requirement: Agent unavailability is non-fatal

Absence or failure of an SSH agent SHALL NOT fail a connection. Resolution SHALL continue using the
remaining credential sources.

#### Scenario: No agent running

- **WHEN** agent authentication is enabled and no agent is reachable
- **THEN** `ISshAgentProvider` returns an empty identity set
- **AND** the resolver continues with configured key path, provider-supplied key material, or secret
- **AND** the connection attempt proceeds

#### Scenario: Agent responds with an error

- **WHEN** the agent is reachable but returns an error or malformed response
- **THEN** the failure is recorded as an informational message
- **AND** resolution continues as though no identities were offered

#### Scenario: Agent is disabled

- **WHEN** agent authentication is disabled for the connection
- **THEN** no agent is contacted
- **AND** the resolved credential contains no agent identities

### Requirement: Agent identities are offered alongside other key sources

Agent identities SHALL be additive. The resolved credential SHALL carry them in addition to any
configured private key path, provider-supplied key material, or discovered default key, ordered
agent-first. An agent identity SHALL NOT suppress any other key source.

An agent holding some identity says nothing about whether it holds *this connection's* key, so
treating agent presence as grounds to withhold another key would break authentication
intermittently. SSH authentication offers candidate keys in turn and lets the server choose, which
is why `PrivateKeyAuthenticationMethod` accepts an array. See design.md D5.

#### Scenario: Agent identities do not suppress provider key material

- **WHEN** an agent offers an identity and an external credential provider also returns private key material
- **THEN** the resolved credential carries both the agent identities and the provider key material

#### Scenario: Agent identities do not suppress a configured key path

- **WHEN** an agent offers an identity and the connection specifies a private key path
- **THEN** the resolved credential carries both the agent identities and that path

#### Scenario: No agent identities available

- **WHEN** no agent identity is available and the connection specifies a private key path
- **THEN** the resolved credential carries that path and no agent identities

### Requirement: Provider-supplied key material is written to a guarded temporary file

The system SHALL mark any materialised private key file as temporary, zero-fill it, and delete it
after the connection attempt. Key material is materialised only for a backend that cannot load a
key any other way.

This records existing behaviour rather than introducing it. Writing a plaintext private key to disk
is a real hazard and is **not** removed by this change: agent identities cannot stand in for a
vault-supplied key, because a key the vault has just minted is by definition not already in the
agent. Removing it requires injecting the key into the agent instead of writing it, which is
deferred to its own change — `add-ssh-agent-key-injection`. See design.md D5.

#### Scenario: Vault key material is materialised and then wiped

- **WHEN** an external credential provider returns private key material for a PuTTY-family connection
- **THEN** the key is written to a file marked temporary
- **AND** the file is zero-filled and deleted after the connection attempt

#### Scenario: No key material means no temporary file

- **WHEN** no provider-supplied key material is present
- **THEN** no temporary private key file is created

### Requirement: Supported agent key types are declared and unsupported ones are filtered

The provider SHALL accept Ed25519, ECDSA P-256/P-384/P-521, and RSA 2048-8192 identities. Until
FIDO/`sk-*` identities are verified against SSH.NET, the provider SHALL exclude them and record that
it did so.

#### Scenario: Supported key type is offered

- **WHEN** the agent holds an Ed25519, ECDSA P-256/P-384/P-521, or RSA 2048-8192 identity
- **THEN** that identity is included in the returned set

#### Scenario: FIDO identity is filtered

- **WHEN** the agent holds an `sk-*` identity
- **THEN** that identity is excluded from the returned set
- **AND** an informational message records that a FIDO identity was skipped and why

#### Scenario: Agent holds only filtered identities

- **WHEN** every identity the agent holds is filtered
- **THEN** the provider returns an empty identity set
- **AND** resolution falls through to the next credential source

### Requirement: Agent authentication is configurable by a single global setting

Agent authentication SHALL be controlled by one application-wide setting. There SHALL NOT be a
per-connection override.

An SSH agent is a user-wide facility, and neither of the external clients mRemoteNG wraps exposes a
per-connection agent toggle — PuTTY consults Pageant unconditionally and `ssh.exe` consults the
Windows agent unconditionally. Per-connection granularity would therefore be a control with no
counterpart in the underlying tools. It remains purely additive if a concrete need appears. See
design.md D10.

#### Scenario: Enabled globally

- **WHEN** the global agent setting is enabled
- **AND** a caller resolves credentials for an SSH.NET-backed connection
- **THEN** the resolution consults the SSH agent

#### Scenario: Disabled globally

- **WHEN** the global agent setting is disabled
- **THEN** no agent is contacted
- **AND** the resolved credential carries no agent identities

#### Scenario: Default is disabled

- **WHEN** the setting has never been changed
- **THEN** agent authentication is disabled

#### Scenario: Backends with a native agent are unaffected

- **WHEN** credentials are resolved for the PuTTY or OpenSSH backend
- **THEN** no agent is contacted through `ISshAgentProvider`
- **AND** the setting does not alter those backends' own native agent behaviour
