## ADDED Requirements

### Requirement: Integration tests supply their own SFTP server

The test suite SHALL run its SFTP integration tests against a server it starts in a container, and SHALL
NOT depend on any server the developer or the runner provides. The container SHALL be started once for
the run and shared, and SHALL be removed when the run ends.

The repository's standing rule is that no test may require a server. A container the test owns satisfies
it rather than breaking it: nothing outside the run has to exist, and nothing outside the run is left
behind. Starting one per test would multiply a few seconds of startup across every case.

#### Scenario: A test run with Docker available

- **WHEN** the SFTP integration tests run
- **THEN** a server container is started once
- **AND** every test in the group connects to it

#### Scenario: The container is cleaned up

- **WHEN** the test run ends, whether or not tests failed
- **THEN** the container is removed

#### Scenario: No external server is required

- **WHEN** the suite is run on a machine with no SFTP server installed
- **THEN** the SFTP integration tests still run

### Requirement: A missing Docker is reported, not passed over

When Docker is unreachable, the SFTP integration tests SHALL fail with a message naming Docker as the
cause. They SHALL NOT report success, and SHALL NOT be silently skipped.

The suite SHALL provide an explicit opt-out for a developer working without Docker, which SHALL be off
by default.

A test that quietly passes when its subject never ran is worse than no test: it reports coverage that
does not exist. CI has Docker, so an unreachable daemon there is a broken runner and should look like
one. The opt-out is deliberate and visible, which is the difference between choosing to skip and not
noticing.

#### Scenario: Docker is not running

- **WHEN** the SFTP integration tests run and Docker cannot be reached
- **THEN** they fail
- **AND** the failure says that Docker is the reason

#### Scenario: A developer opts out

- **WHEN** the opt-out is set
- **THEN** the SFTP integration tests do not run
- **AND** the rest of the suite is unaffected

#### Scenario: The opt-out is off by default

- **WHEN** nothing is configured
- **THEN** the SFTP integration tests run

### Requirement: The session is exercised against a real server

The integration tests SHALL cover, against the container, the operations that a faked `ISftpSession`
cannot answer: connecting, listing, transferring in both directions, and the mutations the session
exposes. They SHALL assert the errors the server produces as well as the successes.

Every existing SFTP test stops at the boundary — the layer that speaks to a server is the layer nothing
has run. These are the cases where being wrong is invisible until somebody connects to a real host.

#### Scenario: Connecting

- **WHEN** the session connects to the container with a password
- **THEN** it reports itself connected
- **AND** its home directory is the one the server placed it in

#### Scenario: Listing

- **WHEN** a directory containing files, subdirectories and a hidden entry is listed
- **THEN** every entry is returned with its kind, size and permission string
- **AND** the `.` and `..` pseudo-entries are not among them

#### Scenario: Transferring

- **WHEN** a file is uploaded and then downloaded again
- **THEN** the downloaded content matches what was uploaded
- **AND** progress was reported during both

#### Scenario: Mutations

- **WHEN** the session renames, deletes, creates a directory and creates a file
- **THEN** a subsequent listing reflects each change

#### Scenario: Errors the server decides

- **WHEN** an operation the server refuses is attempted, such as deleting a non-empty directory or
  listing a path that does not exist
- **THEN** the session surfaces the failure rather than reporting success

#### Scenario: Cancellation

- **WHEN** a transfer is cancelled part-way
- **THEN** it stops

### Requirement: Reconnection is verified against a real connection

The integration tests SHALL reconnect a session repeatedly and assert that each attempt succeeds, that
the session is usable afterwards, and that a connection abandoned by an earlier attempt does not report
itself as dropped against a later one.

This is the specific regression the reconnect work could not cover. `ConnectAsync` replaced its client
and authentication without disposing them, and the ordering fix — unsubscribe before disposing — is
invisible to any test that does not hold a real connection open. It was left to a human reconnecting a
few times and watching, which is not a test.

#### Scenario: Reconnecting repeatedly

- **WHEN** a connected session is reconnected several times in succession
- **THEN** each attempt leaves the session connected and usable

#### Scenario: A replaced connection stays quiet

- **WHEN** a session is reconnected
- **THEN** the previous connection does not report the new session as dropped

#### Scenario: Reconnecting after a real drop

- **WHEN** the connection is broken at the server
- **AND** the session is reconnected
- **THEN** it lists successfully again

### Requirement: Symbolic links are exercised as real links

The integration tests SHALL create real symbolic links on the server and assert that a listing reports
them as links, and that following one distinguishes a link to a directory from a link to a file.

`IsSymbolicLink` and `ResolvesToDirectoryAsync` are the basis of the rule that a recursive transfer never
descends into a link — the rule that stops a link pointing at its own ancestor expanding forever. Both
have only ever been exercised as a fake's boolean, which cannot show that an SFTP listing reports a link
without following it.

#### Scenario: A link in a listing

- **WHEN** a directory containing a symbolic link is listed
- **THEN** the entry is reported as a link

#### Scenario: Following a link to a directory

- **WHEN** a link pointing at a directory is resolved
- **THEN** it is reported as landing on a directory

#### Scenario: Following a link to a file

- **WHEN** a link pointing at a file is resolved
- **THEN** it is reported as not landing on a directory
