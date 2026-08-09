## Why

Connecting a native SSH terminal with no key configured and no password produces this, at **error**
severity, during a connection that then succeeds:

```
The private key file C:\Users\jason\.ssh\id_rsa could not be loaded:
Private key is encrypted but passphrase is empty.
```

Nothing is wrong. The user configured no key, so `SshCredentialResolver` fell back to default key
discovery (`SshCredentialResolver.cs:172`), found `~/.ssh/id_rsa`, and `SshNetAuthAdapter` could not
load it because it is passphrase-protected — which is the normal state of a personal SSH key. The
agent supplied the identity that actually authenticated.

The credential model carries a path but not where it came from, so
`SshNetAuthAdapter.CollectKeyFile` cannot tell a key the user chose from one the application went
looking for. It reports both identically:

- at `SshCredentialDiagnosticSeverity.Error`, which callers surface as `MessageClass.ErrorMsg`
- worded *"The **configured** private key file was not found"*, which is untrue of a discovered key

A key the user selected failing to load is an error they must act on. A key the application guessed
at failing to load is not their problem and usually not actionable — most people's `~/.ssh/id_rsa`
has a passphrase. Reporting the second as the first trains users to ignore the channel that carries
the first.

Found while verifying task 8.5 of `add-native-ssh-terminal`: the message appeared during a
**successful** connection and was mistaken for its cause, costing a round of diagnosis.

A second defect in the same file, found in the same session: an empty username throws
`ArgumentException` out of `PrivateKeyAuthenticationMethod`'s constructor rather than being reported.
A connection with no username is a configuration mistake with an obvious message; it currently
surfaces as an unhandled exception from a credential adapter.

## What Changes

- `ResolvedSshCredential` records **how** its key path was obtained — chosen by the user, or found by
  discovery.
- `SshNetAuthAdapter` reports a discovered key it cannot use as information rather than error, and
  words it as a discovered key. Behaviour for a configured key is unchanged.
- A missing username is reported as a diagnostic instead of throwing.
- No change to which credentials are offered, or in what order. This is about what is *said*.

## Capabilities

Modifies `ssh-credential-resolution`, introduced by `add-ssh-agent-credential-resolver` and not yet
archived. That change owns the requirement that diagnostics are replayed on the channel each records;
this one makes the recorded severity fit what actually happened.

## Impact

`SshNetAuthAdapter` and `ResolvedSshCredential` are shared by every SSH.NET-backed caller, so the
change reaches further than the terminal that exposed it:

- `mRemoteNG/Connection/Sftp/SftpSession.cs`
- `mRemoteNG/Tools/SecureTransfer.cs`
- `mRemoteNG/Connection/Protocol/SSH/Native/NativeSshTerminalSession.cs`
- `mRemoteNG/UI/Window/FileManagerTab.cs` and `PuttyBase`/`ProtocolOpenSSH`, which replay diagnostics

All of them get quieter for the same reason, and none of them change what they attempt. The risk is
the reverse of the defect: a genuinely misconfigured key being downgraded to information. That is why
provenance is recorded at the point the path is set rather than inferred later from the path's shape.
