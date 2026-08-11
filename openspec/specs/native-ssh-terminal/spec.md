# native-ssh-terminal Specification

## Purpose
An SSH terminal hosted inside mRemoteNG — SSH.NET for the transport, xterm.js in WebView2 for the
emulator — selectable per connection and running alongside the PuTTY-backed SSH2 protocol rather
than replacing it. PuTTY remains the default: see the archived change's design.md for what PuTTY
still does better, chiefly that keyboard-interactive prompts cannot be answered and there is no
session logging.
## Requirements
### Requirement: A connection may use a native SSH terminal instead of PuTTY

The system SHALL offer an SSH terminal hosted in-process, selectable per connection, and SHALL leave
the existing PuTTY-backed SSH2 protocol available and unchanged.

Replacing SSH2 outright would migrate every existing SSH connection onto an emulator that has not
been exercised in real use, with no way back. Opting in per connection lets the two run side by side
until the native terminal has earned the default.

#### Scenario: A connection selects the native terminal

- **WHEN** a connection is configured to use the native SSH terminal
- **THEN** the session is established by the in-process SSH client
- **AND** no external terminal process is started

#### Scenario: Existing SSH2 connections are unaffected

- **WHEN** a connection is configured as SSH2
- **THEN** it continues to use the PuTTY-backed protocol
- **AND** its behaviour is unchanged by this feature

#### Scenario: The web runtime is missing

- **WHEN** the native terminal is selected and the WebView2 runtime is not installed
- **THEN** the connection fails with a message naming the missing runtime and how to install it
- **AND** the failure is not reported as an authentication or network error

### Requirement: The terminal authenticates through the shared credential model

The native terminal SHALL obtain credentials through `ISshCredentialResolver` and SHALL authenticate
using the SSH.NET adapter, so that a connection authenticates identically whichever SSH backend it
uses.

This is the reason the credential model was made backend-neutral. A terminal with its own credential
path would reintroduce exactly the divergence that change removed, where each backend supported a
different subset of credential providers.

#### Scenario: Provider-supplied credentials reach the terminal

- **WHEN** a connection using the native terminal has an external credential provider configured
- **THEN** the resolved credential is used to authenticate the session

#### Scenario: Agent identities are offered

- **WHEN** SSH agent support is enabled and the agent holds identities
- **THEN** those identities are offered when the terminal authenticates

#### Scenario: Resolution diagnostics are reported

- **WHEN** credential resolution produces diagnostics
- **THEN** they are reported on the connection's message channel

### Requirement: The remote terminal tracks the size of the terminal control

The system SHALL send the terminal's current dimensions to the remote pseudo-terminal when the
session starts and whenever the control is resized.

A terminal whose remote size does not match its visible size renders full-screen applications —
editors, pagers, anything using curses — incorrectly, which makes the terminal unusable rather than
merely imperfect.

#### Scenario: Initial size

- **WHEN** a native terminal session is established
- **THEN** the remote pseudo-terminal is created with the control's current dimensions

#### Scenario: The control is resized

- **WHEN** the terminal control's size changes
- **THEN** the new dimensions are sent to the remote pseudo-terminal

#### Scenario: Resizing while disconnected

- **WHEN** the terminal control is resized and no session is established
- **THEN** no resize is sent
- **AND** no error is raised

### Requirement: Terminal output and input are carried faithfully

The system SHALL pass bytes between the remote shell and the terminal emulator without altering
them, and SHALL treat the stream as UTF-8.

#### Scenario: Output is displayed

- **WHEN** the remote shell writes output
- **THEN** it is rendered by the terminal

#### Scenario: Input is sent

- **WHEN** the user types in the terminal
- **THEN** the corresponding bytes are sent to the remote shell

#### Scenario: Multi-byte characters

- **WHEN** output contains multi-byte UTF-8 sequences
- **THEN** they are rendered as the characters they encode
- **AND** a sequence split across two reads is not corrupted

#### Scenario: The session ends remotely

- **WHEN** the remote shell exits
- **THEN** the connection is reported as disconnected

### Requirement: The terminal front end loads no remote content

All terminal front-end assets SHALL be served from within the application, and the terminal host
SHALL NOT load script, style, font or image resources over the network.

The terminal renders output from a remote host inside a browser engine. Any path that lets remote
content reach the network, or lets it be treated as anything other than text, turns terminal output
into a code execution surface.

#### Scenario: Assets are local

- **WHEN** the terminal host initialises
- **THEN** every script, stylesheet and font it loads comes from within the application

#### Scenario: Remote output is not interpreted as markup

- **WHEN** terminal output contains HTML or script markup
- **THEN** it is rendered as literal terminal text
- **AND** it is not evaluated by the host

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

### Requirement: Terminal appearance is configurable

The system SHALL allow the terminal font, colour scheme and scrollback length to be configured, and
SHALL apply the configured values to new sessions.

#### Scenario: Configured font is applied

- **WHEN** a terminal font is configured
- **AND** a native terminal session is opened
- **THEN** the terminal renders in that font

#### Scenario: Scrollback length is honoured

- **WHEN** a scrollback length is configured
- **THEN** the terminal retains at most that many lines

