## MODIFIED Requirements

### Requirement: The host key is presented for confirmation

The system SHALL present an unknown or changed host key to the user for confirmation before **any**
SSH connection it opens proceeds, SHALL NOT accept host keys silently, and SHALL refuse the
connection where no confirmation can be obtained.

PuTTY previously owned this. Hosting the transport in-process means inheriting the responsibility,
and silently accepting host keys would remove a protection users currently have.

Stated for every connection rather than for the terminal session, because the application opens more
than one. The file manager and the file transfer window each open their own SSH connection to the
same host, and a rule worded around "the session" left both of them outside it — which is how they
were built with no host key verification at all while the session beside them verified carefully.

Acceptance is recorded against the host, not the connection that obtained it, so a user is asked once
per host rather than once per feature.

#### Scenario: An unknown host key

- **WHEN** connecting to a host whose key is not known
- **THEN** the key fingerprint is presented for confirmation
- **AND** the connection proceeds only if the user accepts

#### Scenario: A changed host key

- **WHEN** a host presents a key differing from the stored one
- **THEN** the change is reported prominently
- **AND** the connection proceeds only if the user accepts

#### Scenario: A known host key

- **WHEN** a host presents its already-accepted key
- **THEN** the connection proceeds without prompting

#### Scenario: A second connection to a host already accepted

- **WHEN** the file manager or the file transfer window connects to a host whose key was accepted for
  a terminal session
- **THEN** it proceeds without prompting again

#### Scenario: A second connection where the key is not the accepted one

- **WHEN** the file manager or the file transfer window connects to a host presenting a key differing
  from the stored one
- **THEN** it is refused on the same terms as a terminal session

#### Scenario: A consumer that cannot prompt

- **WHEN** an SSH connection is opened where no confirmation can be presented
- **THEN** an unknown or changed key is refused
- **AND** the connection does not proceed
