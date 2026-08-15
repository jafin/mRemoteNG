# connection-record-secrets Specification

## Purpose

How long a connection's secrets exist as plain text in this process, and where.

Most of what this capability states was already true and simply undocumented — which is why an
automated scan reads the `string` property type, concludes the storage is a plain field, and reports
this fork as carrying a CVE it does not. Writing the guarantees down is half the point: a refactor
that "simplified" the setter back to a plain field would reintroduce the defect and pass every other
test in the suite.

The other half is the boundaries. A secret has to become a `string` wherever something that cannot
take a `SecureString` needs it — the property grid, the RDP client's COM surface, the external
credential providers — and each of those copies is immutable and cannot be zeroed. What this
capability governs is that the copy is made where it is needed and not before, and that the routes
which never needed one do not create it.

`SecureString` is not a security boundary and Microsoft documents it as not being one. An attacker
who can read this process's memory has already won by most measures. What is bought here is narrower
and still worth having: fewer copies, alive for less time, in the paths a user exercises most.

**One exposure is known and open.** Loading a connection file decrypts every password in it at once,
through a plain `string[]`, before any of them reaches a `SecureString`. That is wider than anything
this capability currently narrows, and closing it means decrypting per record on demand — its own
change, with its own risk to whether connections authenticate at all.

## Requirements
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

### Requirement: A decrypted secret is held only as a SecureString

The system SHALL place a secret decrypted from the connection file into the record's `SecureString`
storage, and SHALL NOT retain it in a plain-text field on the record.

**This replaces a requirement stating that secrets are decrypted per field rather than eagerly,
which was proposed on a false premise and is not met.** The proposal cited a deferral in the
deserializer; that deferral is real but it collects every encrypted attribute during the XML walk and
decrypts *all of them in one batch* before the load returns. It batches the key derivation for speed
and does nothing for lifetime. Opening a connection file therefore does place every password in the
process at once, and the batch materialises them as a `string[]` — immutable, unzeroable, alive until
collected — before each is copied into its record's `SecureString`.

That is a wider exposure than the property-getter copies this capability narrows, and it is recorded
here rather than quietly dropped. Closing it means making decryption lazy per record, which changes
how the deserializer, the tree model and every consumer of a loaded record behave, and belongs to its
own change with its own risk assessment. `ConnectionSecretDecryptionTimingTests` pins the current
behaviour so the day it changes is visible.

#### Scenario: Opening a connection file

- **WHEN** a connection file is loaded
- **THEN** each decrypted secret is held on its record as a `SecureString`
- **AND** no record holds a secret in a plain-text field

### Requirement: Callers that can consume a SecureString are given one

The connection record SHALL expose its secrets as `SecureString` for callers able to consume them,
and the plain-text property SHALL exist only for the property grid and interop that requires a
string.

Converting to `string` and back produces immutable copies that cannot be zeroed. Where the consumer
accepts a `SecureString` the round trip creates the exposure it is meant to avoid.

**The named consumer is the credential store, not SSH.** The proposal expected `SshCredentialResolver`
to be the adopter; it deals in `string` throughout — the external credential providers return strings,
the default password unprotects to one, and `ResolvedSshCredential` takes a string and converts it to
a `char[]` it zeroes, with a remark already recording that the string entry point is out of scope.
Handing that path a `SecureString` would mean converting back for every provider branch, which is
worse than what it does today.

The real round trip is at the credential-record boundary, and it is worse than the one proposed:
a connection bound to a credential record converted that record's `SecureString` into a plain string
on *every* read of its password, and the property grid re-reads a displayed connection constantly.

Where a secret resolves through inheritance or a connection link, the plain-text property is used
deliberately: those routes answer by reading another record's string property, including a walk that
skips parents holding an empty credential, and a second implementation of those rules could drift
from the first. An accessor that returns a different password than the property beside it is a worse
outcome than the copy it would save.

#### Scenario: A caller reads a password backed by a credential record

- **WHEN** a caller able to consume a `SecureString` requires a connection's password
- **AND** the connection is bound to a credential record
- **THEN** it obtains a `SecureString` copy of the credential's own secret
- **AND** no plain-text copy is created on its behalf

#### Scenario: A caller disposes what it was given

- **WHEN** a caller disposes the `SecureString` it obtained
- **THEN** the connection record's own secret is unaffected
- **AND** the credential record's secret is unaffected

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

