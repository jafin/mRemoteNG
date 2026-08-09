## ADDED Requirements

### Requirement: Single credential resolution path for all SSH backends

The system SHALL resolve SSH credentials for a connection through one service,
`ISshCredentialResolver`, which accepts a `ConnectionInfo` and returns a backend-neutral
`ResolvedSshCredential`. No SSH backend SHALL implement its own external credential provider logic.

#### Scenario: PuTTY backend resolves through the shared service

- **WHEN** a connection using a PuTTY-family protocol (SSH1, SSH2, Telnet, Rlogin, RAW, Serial) is opened
- **THEN** `PuttyBase` obtains credentials by calling `ISshCredentialResolver`
- **AND** `PuttyBase` contains no direct reference to any external credential provider

#### Scenario: OpenSSH backend resolves through the same service

- **WHEN** a connection using `ProtocolType.OpenSSH` is opened
- **THEN** `ProtocolOpenSSH` obtains credentials by calling `ISshCredentialResolver`
- **AND** the resolved username reflects the same fallback and domain-qualification rules applied to PuTTY connections

#### Scenario: Resolved credential is backend-neutral

- **WHEN** `ISshCredentialResolver` returns a `ResolvedSshCredential`
- **THEN** the returned value exposes username, optional secret, optional key material, agent identities, and provider provenance
- **AND** it exposes no PuTTY command-line arguments, no OpenSSH command-line arguments, and no SSH.NET `AuthenticationMethod` instances

### Requirement: All external credential providers are supported by the resolver

The resolver SHALL support every external credential provider mRemoteNG currently implements:
Delinea Secret Server, Clickstudios Passwordstate, 1Password, PasswordSafe, Vault/OpenBao
(password engine and SSH-OTP engine), and LAPS.

#### Scenario: Provider supplies a password

- **WHEN** a connection specifies an external credential provider that returns a password
- **THEN** the resolver returns a `ResolvedSshCredential` whose secret is that password
- **AND** whose provenance identifies the provider that answered

#### Scenario: Provider supplies a private key

- **WHEN** a connection specifies an external credential provider that returns private key material
- **THEN** the resolver returns a `ResolvedSshCredential` carrying that key material
- **AND** the key material is not written to disk by the resolver

#### Scenario: Provider fails

- **WHEN** an external credential provider throws during resolution
- **THEN** the resolver records the failure as an error message identifying the provider
- **AND** returns a `ResolvedSshCredential` reflecting the remaining available credentials rather than throwing

### Requirement: Username fallback and domain qualification are applied once

The resolver SHALL apply empty-credential fallback rules and domain qualification, and SHALL return
the effective username. Backends SHALL NOT re-apply these rules.

#### Scenario: Empty username falls back to the Windows user

- **WHEN** a connection has no username and the empty-credentials setting is `windows`
- **THEN** the resolved username is the current Windows user name

#### Scenario: Empty username falls back to a configured default

- **WHEN** a connection has no username, the empty-credentials setting is `custom`, and a default username is configured
- **THEN** the resolved username is the configured default username

#### Scenario: Domain-qualified username

- **WHEN** a connection specifies both a domain and a username that contains neither `\` nor `@`
- **THEN** the resolved username is `domain\username`

#### Scenario: Already-qualified username is not re-qualified

- **WHEN** a connection specifies a domain and a username already containing `\` or `@`
- **THEN** the resolved username is returned unchanged

### Requirement: Backend adapters translate the neutral credential

The system SHALL provide one adapter per SSH backend that translates a `ResolvedSshCredential` into
that backend's required form. Adapters SHALL be the only backend-aware credential code.

#### Scenario: PuTTY adapter emits PuTTY arguments

- **WHEN** the PuTTY adapter translates a `ResolvedSshCredential` containing a secret
- **THEN** it emits the existing PuTTY credential arguments, using `-pwfile` with a named pipe for PuTTY 0.81 and later and `-pw` otherwise

#### Scenario: SSH.NET adapter emits authentication methods

- **WHEN** the SSH.NET adapter translates a `ResolvedSshCredential`
- **THEN** it emits an `AuthenticationMethod[]` suitable for `ConnectionInfo`
- **AND** agent identities are emitted as a `PrivateKeyAuthenticationMethod`
- **AND** a `KeyboardInteractiveAuthenticationMethod` is included so a residual server challenge can still be answered

### Requirement: Adapters report credentials they cannot honour

An adapter SHALL report any component of a `ResolvedSshCredential` it cannot translate, and the
caller SHALL surface that as a warning naming both the credential source and the backend.

#### Scenario: Password cannot be passed to ssh.exe

- **WHEN** a connection using `ProtocolType.OpenSSH` resolves to a credential containing a secret
- **THEN** the OpenSSH adapter reports the secret as unsupported
- **AND** a warning message is emitted naming the credential provider and stating that the OpenSSH backend cannot accept a password non-interactively
- **AND** the connection still proceeds using the remaining credential components

#### Scenario: Fully supported credential produces no warning

- **WHEN** an adapter can translate every component of a `ResolvedSshCredential`
- **THEN** no unsupported-credential warning is emitted

### Requirement: Resolved credentials are not leaked through logs or lifetime

`ResolvedSshCredential` SHALL be disposable, SHALL zero its secret buffers on disposal, and SHALL
never be written to any message writer or log.

#### Scenario: Secret is cleared on disposal

- **WHEN** a `ResolvedSshCredential` holding a secret is disposed
- **THEN** the buffer backing that secret is zeroed

#### Scenario: Provenance logging excludes secrets

- **WHEN** the resolver logs which provider answered
- **THEN** the message contains the provider name
- **AND** the message contains neither the secret nor any key material
