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

Acceptance is recorded against the endpoint — host, port and host key algorithm together — and not
against the connection that obtained it, so a user is asked once per endpoint rather than once per
feature. The tuple is what the store already keys on, and it is the honest unit: a different port is
a different service, and a key of a different algorithm is a different key. Both are legitimate
reasons to be asked again, and a prompt in either case is not evidence that the record was lost.

Trust is shared; the prompt is not. Every consumer SHALL consult the same persistent store, so a key
accepted anywhere in the application is known everywhere in it. The prompt belongs to whichever
window owns the connection, because a dialog has to appear over the thing the user is looking at.
The two are separable and are separated deliberately: a consumer that cannot prompt still reads the
shared record, so it proceeds on a known key and refuses only what it would have had to ask about.

#### Scenario: An unknown host key

- **WHEN** connecting to a host whose key is not known
- **THEN** the key fingerprint is presented for confirmation
- **AND** the connection proceeds only if the user accepts

#### Scenario: A changed host key

- **WHEN** a host presents a key differing from the stored one
- **THEN** the change is reported prominently
- **AND** the connection proceeds only if the user accepts

#### Scenario: A changed host key that is accepted

- **WHEN** the user accepts a changed key for an endpoint
- **THEN** the accepted fingerprint replaces the stored one for that endpoint
- **AND** a later connection presenting the previously stored fingerprint is treated as changed and
  asked about again, not accepted silently

#### Scenario: An endpoint differing only in port or key algorithm

- **WHEN** a connection is made to a host already accepted, but on a different port or negotiating a
  different host key algorithm
- **THEN** the key is treated as unknown and presented for confirmation

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
- **AND** a key already accepted for that endpoint still proceeds, because no confirmation is needed
  to honour a decision the user has already made

#### Scenario: Two connections to one endpoint at the same time

- **WHEN** two SSH connections to the same endpoint are opened concurrently and its key is unknown
- **THEN** the user is asked once
- **AND** both connections take the answer given, rather than each presenting its own prompt
