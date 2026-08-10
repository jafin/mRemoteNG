## ADDED Requirements

### Requirement: The connection file states the key derivation parameters it was written with

The connection file SHALL record the PBKDF2 pseudo-random function used to derive its key, alongside
the iteration count it already records, and the reader SHALL derive using the parameters the file
states rather than the parameters currently configured.

A file outlives the build that wrote it. Parameters that are read from configuration at open time
cannot be changed without making every existing file unreadable, which is why the iteration count is
already stored in the file; the PRF is the other half of the same pair and was left implicit.

#### Scenario: A file written by this version

- **WHEN** the connection file is saved
- **THEN** the root element carries the pseudo-random function alongside the iteration count
- **AND** the recorded function is the one used to derive the key

#### Scenario: A file that records a pseudo-random function

- **WHEN** a connection file is opened and records a pseudo-random function
- **THEN** the key is derived with that function
- **AND** the currently configured value is not consulted

### Requirement: A file that predates the recorded PRF is read as SHA-1

The system SHALL derive with PBKDF2-HMAC-SHA1 when a connection file records no pseudo-random
function.

Every file written before this change used SHA-1 and says nothing about it. Absence has to keep
meaning SHA-1 or those files stop opening, which for a connection manager means the user loses their
connections with no way to tell why.

#### Scenario: A file with no recorded function

- **WHEN** a connection file records no pseudo-random function
- **THEN** the key is derived with HMAC-SHA1
- **AND** the file opens with no migration step and no prompt

#### Scenario: A file with no recorded function is saved

- **WHEN** such a file is opened and then saved
- **THEN** it is rewritten with the current function recorded
- **AND** it is no longer readable as SHA-1

### Requirement: New connection files are written with HMAC-SHA256

The system SHALL derive keys for newly written connection files using PBKDF2-HMAC-SHA256.

The 600,000 iterations already configured is OWASP's guidance for HMAC-SHA256. Against HMAC-SHA1 the
same count is roughly half the intended work factor, so pairing the count with the function it was
chosen for costs nothing at runtime and closes the gap without doubling the time to open a file.

#### Scenario: Deriving a key for a new file

- **WHEN** a key is derived for a connection file being written
- **THEN** the pseudo-random function is HMAC-SHA256
- **AND** the iteration count is the configured value

### Requirement: The key derivation function is given its parameters explicitly

`Pkcs5S2KeyGenerator` SHALL require an iteration count and a pseudo-random function from its caller,
and SHALL NOT supply a default for either.

A defaulted iteration count is indistinguishable at the call site from a chosen one. The existing
default of 1000 is three orders of magnitude below the configured value and would silently produce a
weak key from a `new()` that merely forgot an argument.

#### Scenario: Constructing without an iteration count

- **WHEN** the key derivation function is constructed
- **THEN** an iteration count and a pseudo-random function must both be supplied

### Requirement: Derived key caching accounts for the pseudo-random function

The system SHALL treat a change of pseudo-random function as a cache miss when reusing a derived key.

`AeadCryptographyProvider` caches a derived key against the password, salt and iteration count so
that saving a file derives once rather than once per field. Two files can share all three and differ
only in the function, so a cache that ignores it would encrypt one file with the other's key.

#### Scenario: Two derivations differing only by function

- **WHEN** a key is derived, and a second derivation matches on password, salt and iteration count
- **AND** the pseudo-random function differs
- **THEN** the cached key is not reused
- **AND** the key is derived again with the requested function
