# ssh-credential-resolution Specification

## Purpose

How an SSH connection's credentials are resolved into something a backend can authenticate with, and
how problems found along the way are reported. Covers where a private key came from, which key
failures are the user's business, and what happens when a credential is incomplete.

## Requirements

### Requirement: A resolved credential records where its private key path came from

`ResolvedSshCredential` SHALL record whether its private key path was configured on the connection or
found by default key discovery, and the resolver SHALL set that when it sets the path.

Backends cannot report a key problem accurately without knowing whether the user chose the key. The
distinction exists only at the point the path is assigned, so it must be captured there rather than
inferred later from the path itself.

#### Scenario: A key configured on the connection

- **WHEN** a connection specifies a private key path
- **THEN** the resolved credential reports that path as configured

#### Scenario: A key found by discovery

- **WHEN** a connection specifies no private key, no usable key material and no usable secret
- **AND** default key discovery locates a key
- **THEN** the resolved credential reports that path as discovered

#### Scenario: No key at all

- **WHEN** no key path is configured and discovery locates nothing
- **THEN** the resolved credential carries no key path, and nothing is reported about one

### Requirement: A discovered key that cannot be used is reported as information

The system SHALL report at information severity any private key obtained by discovery that cannot be
turned into an authentication method, and SHALL NOT describe such a key as configured.

A passphrase-protected `~/.ssh/id_rsa` is the ordinary state of a personal key. Reporting it as an
error during a connection that authenticates by other means makes a successful connection look
failed, and teaches users to disregard the channel that carries real credential errors.

#### Scenario: A discovered key is passphrase-protected

- **WHEN** discovery locates a key that cannot be loaded without a passphrase
- **AND** no passphrase is available
- **THEN** the failure is reported at information severity
- **AND** the message does not call the key configured
- **AND** the connection continues with whatever other credentials are available

#### Scenario: A discovered key is unreadable

- **WHEN** discovery locates a key that cannot be read or parsed
- **THEN** the failure is reported at information severity
- **AND** the message identifies the file so a user who wants it used can act

#### Scenario: A discovered key is usable

- **WHEN** discovery locates a key that loads
- **THEN** it is offered for authentication
- **AND** nothing is reported about it

### Requirement: A configured key that cannot be used is still reported as an error

The system SHALL report at error severity any private key named by the connection that cannot be
turned into an authentication method.

This is the case the existing severity is right for, and the reason the change is narrowed to
discovered keys: a user who selected a key and got no authentication from it must be told plainly.

#### Scenario: A configured key is missing

- **WHEN** a connection names a private key file that does not exist
- **THEN** the failure is reported at error severity
- **AND** the message names the file

#### Scenario: A configured key cannot be loaded

- **WHEN** a connection names a private key file that cannot be loaded
- **THEN** the failure is reported at error severity

### Requirement: A missing username is reported rather than thrown

The system SHALL report a credential with no username as a diagnostic, and SHALL NOT raise an
exception while building authentication methods.

Translation is treated as total by every caller, so an exception from it surfaces as an unhandled
failure inside a credential adapter instead of the configuration mistake it describes.

#### Scenario: A connection with no username

- **WHEN** a credential is translated with an empty or whitespace username
- **THEN** a diagnostic reports that the connection has no username
- **AND** no exception is raised
- **AND** no authentication method is built from it
