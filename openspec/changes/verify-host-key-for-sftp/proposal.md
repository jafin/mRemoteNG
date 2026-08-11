# Verify the host key on every SSH connection, not only the terminal session

## Why

The native SSH terminal verifies host keys. Nothing else does.

```csharp
HostKeyGate hostKeys = new(new FileHostKeyStore(), new DialogHostKeyVerifier(_webView));
```

`ProtocolNativeSsh.cs:258`

`SftpSession` — the file manager panel — contains **no host key handling at all**, and neither does
`SSHTransferWindow`. Neither subscribes to `HostKeyReceived`, and SSH.NET accepts any host key when
nothing does. Both open their own SSH connection to the host.

So a user opens a terminal session, is shown a fingerprint, accepts it, and mRemoteNG stores it.
Then they open the file manager onto the same host — and that connection accepts **whatever key is
offered**, including a different one. The protection the session carefully provides is absent from
the connection sitting next to it in the same window.

`add-sftp-browser-panel` task 1.2 measured the file manager as costing zero prompts on top of the
session. Part of that zero is this: not "asked once and remembered", but never asked, because
nothing checks.

### Why it is not covered today

The requirement exists, but is worded around the terminal:

> The system SHALL present an unknown or changed host key to the user for confirmation before **the
> session** proceeds, and SHALL NOT accept host keys silently.

`native-ssh-terminal` spec. Its stated reason — "silently accepting host keys would remove a
protection users currently have" — applies to every SSH connection the application makes. The wording
does not, which is how two consumers were built without it and neither review caught it.

### Not only the new panel

`SSHTransferWindow` predates the file manager and has the same gap, so this is not a regression
introduced by `add-sftp-browser-panel` — that change made it visible by putting a second connection
beside a verified one.

## What Changes

- The host key requirement is restated to cover **every** SSH connection the application opens, with
  the terminal session as one case rather than the rule.
- `SftpSession` and `SSHTransferWindow` verify through the same `HostKeyGate` and `FileHostKeyStore`
  the session uses.
- A host already accepted stays silent. `FileHostKeyStore` persists the acceptance, so a second
  connection to a known host prompts for nothing — the measured cost of opening the panel stays zero
  where it is zero today, and a prompt appears only when the host is genuinely unknown or changed,
  which is when one is wanted.
- A consumer that cannot prompt fails closed. `NativeSshTerminalSession` already defaults to
  `DenyUnverifiedHostKeys` when no gate is supplied; the same default applies here rather than
  inventing a quieter one.

### The open question this change has to answer

`DialogHostKeyVerifier` is constructed with the terminal's WebView so the prompt appears over the tab
the user is looking at. The file manager has no WebView. Whether the panel gets its own verifier
bound to its own control, or whether a shared verifier is owned above both, is the design work here —
it is not a matter of passing the existing object through.

## Impact

`mRemoteNG/Connection/Sftp/SftpSession.cs`, `mRemoteNG/UI/Window/SSHTransferWindow.cs`,
`mRemoteNG/Connection/Protocol/SSH/Native/HostKeys/`.

Affects `native-ssh-terminal`. Users who have already accepted a host key see no change; users
connecting the file manager to an unknown host will be asked, where today they are not.
