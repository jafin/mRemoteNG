## ADDED Requirements

### Requirement: Connection record secrets are held as SecureString

The connection record SHALL store its password, gateway password and proxy password as `SecureString`
and SHALL dispose the previous value when one is replaced.

This is already true and is stated so it survives. The property type is `string` for the property
grid's benefit, which makes the storage look like a plain field to a reader and to an automated scan;
a future refactor that "simplified" the setter back to a plain field would reintroduce the defect and
pass every existing test.

#### Scenario: Assigning a secret

- **WHEN** a connection record's password is set
- **THEN** the value is held as a `SecureString`
- **AND** the previously held value is disposed

#### Scenario: Assigning the same value

- **WHEN** a password is set to the value it already holds
- **THEN** the stored value is not replaced
- **AND** no change notification is raised

### Requirement: Secrets are decrypted per field rather than eagerly

The system SHALL decrypt a stored secret when that record's secret is required, and SHALL NOT
decrypt every secret in the connection file when the file is opened.

Opening a connection file would otherwise place every password in the process at once, for the
lifetime of the session, to serve the connections a user actually opens — usually a handful.

#### Scenario: Opening a connection file

- **WHEN** a connection file is loaded
- **THEN** secrets are not decrypted as part of loading

### Requirement: Callers that can consume a SecureString are given one

The connection record SHALL expose its secrets as `SecureString` for callers able to consume them,
and the plain-text property SHALL exist only for the property grid and interop that requires a
string.

Converting to `string` and back produces immutable copies that cannot be zeroed. Where the consumer
accepts a `SecureString` — SSH.NET's credential path and the transfer backends built on it — the
round trip creates the exposure it is meant to avoid.

#### Scenario: An SSH-backed caller reads a password

- **WHEN** a caller able to consume a `SecureString` requires a connection's password
- **THEN** it obtains a `SecureString`
- **AND** no plain-text copy is created on its behalf

#### Scenario: The property grid displays a connection

- **WHEN** the property grid binds to a connection record
- **THEN** the plain-text property continues to work as it does today

### Requirement: Plain-text conversion happens as late as possible

Where interop requires a plain-text secret, the system SHALL convert at the point of use rather than
holding the converted value across unrelated work.

The RDP client's COM surface takes a `string`, so one copy is unavoidable. Creating it before the
external-credential-provider branches and using it afterwards extends its lifetime across work that
never needed it, for no benefit.

#### Scenario: Assigning a password to the RDP client

- **WHEN** a password is supplied to the RDP client
- **THEN** the plain-text value is obtained immediately before the assignment

#### Scenario: An external credential provider supplies the password

- **WHEN** the password comes from an external credential provider
- **THEN** the connection record's own secret is not converted to plain text
