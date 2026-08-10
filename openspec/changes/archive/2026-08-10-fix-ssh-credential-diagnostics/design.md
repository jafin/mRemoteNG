# Design

## Context

`SshCredentialResolver` produces a `ResolvedSshCredential` whose `PrivateKeyPath` comes from one of
two places:

| Source | Where |
|---|---|
| The user's connection | `connectionInfo.PrivateKeyPath` |
| Default discovery | `_keyLocator.Locate(options.DefaultKeyDiscovery)` — `SshCredentialResolver.cs:172` |

Discovery runs only when nothing else can authenticate: no materialised key material, no configured
path, and no secret the backend could use. That gate is correct and is not changing.

`SshNetAuthAdapter.CollectKeyFile` then loads the file and, on failure, records a diagnostic at
`Error` severity worded *"The configured private key file …"*. It has no way to know the path was
never configured.

## Goals

- A discovered key that cannot be used stops looking like a failure.
- A configured key that cannot be used keeps looking exactly as it does now.
- No change to which authentication methods are offered.

## Non-Goals

- Changing discovery itself — which keys are looked for, or when.
- Loading passphrase-protected discovered keys by prompting. A prompt during discovery would ask
  about a key the user never mentioned; if they want it used, they can configure it.
- Reworking the diagnostic channels. `SshCredentialDiagnostic` already carries severity per message
  and callers already replay it; only the value assigned is wrong.

## Decisions

### D1 — Record provenance, do not infer it

`ResolvedSshCredential` gains a flag saying whether `PrivateKeyPath` was configured or discovered,
set where the path is assigned.

*Alternative rejected:* have the adapter guess — treat a path under `~/.ssh` as discovered. It is
wrong in both directions. A user may deliberately configure `~/.ssh/id_ed25519`, and
`IDefaultSshKeyLocator` is an interface whose implementation may look elsewhere. Guessing would also
put the knowledge in the layer that does not have it, which is the defect being fixed.

### D2 — Information, not warning

A discovered key that will not load is downgraded to `Information`, not `Warning`.

Warning implies something the user should act on. They should not: a passphrase-protected
`~/.ssh/id_rsa` is the ordinary state of a personal key, and the connection either authenticates
another way or fails with its own message. The line is worth keeping at all only because it explains
why a key present on disk took no part.

### D3 — The wording changes with the severity

*"The configured private key file was not found"* is false for a discovered path and misdirects the
reader to a setting they never set. Discovered-key messages say the key was found by discovery and
name where. Configured-key messages are untouched, so existing translations and any log-scraping keep
working for the case that matters.

### D4 — A missing username is a diagnostic, not an exception

`AuthenticationMethod`'s constructor throws `ArgumentException` on an empty username, so
`SshNetAuthAdapter.Translate` throws before returning. Callers treat translation as total and do not
guard it, so a connection with no username fails as an unhandled exception out of a credential
adapter rather than as "this connection has no username".

Translation reports it the way it reports every other unusable credential component: a diagnostic,
and no method built from it. If nothing else can authenticate, the connection fails on its own terms
with the diagnostic attached — which is exactly the path `add-native-ssh-terminal` built for refusals.

## Risks

- **Under-reporting.** A user who configures a key badly must still be told loudly. Provenance is
  recorded where the path is set, so the configured path cannot be reclassified by accident; a test
  pins each direction.
- **Shared blast radius.** Every SSH.NET-backed caller replays these diagnostics. All of them get
  quieter for the same reason, and none change what they attempt — but SFTP, `SecureTransfer` and the
  terminal should each be exercised before this is called done.
